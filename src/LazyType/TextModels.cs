namespace LazyType;

internal static class TextModels
{
    public const string Qwen35 = "Qwen3.5 9B";
    public const string Gemma = "Gemma 4 12B";
    public static readonly string[] Choices = { Qwen35, Gemma };
    public static string Normalize(string? choice) => Choices.Contains(choice) ? choice! : Qwen35;

    public static string PathFor(string choice)
    {
        var (name, downloaded) = Normalize(choice) switch
        {
            Gemma => ("gemma4-12b-q4.gguf", "gemma-4-12b-it-Q4_K_M.gguf"),
            _ => ("qwen3.5-9b-q4.gguf", "Qwen3.5-9B-Q4_K_M.gguf")
        };
        var installed = Path.Combine(AppSettings.Root, "models", name);
        if (File.Exists(installed)) return installed;
        // Reuse the benchmark downloads without copying multi-gigabyte files.
        var benchmarks = Path.Combine(AppSettings.Root, "benchmarks");
        if (Directory.Exists(benchmarks))
            foreach (var folder in Directory.GetDirectories(benchmarks).OrderByDescending(p => p, StringComparer.Ordinal))
            {
                var existing = Path.Combine(folder, "models", downloaded);
                if (File.Exists(existing)) return existing;
            }
        return installed;
    }
}

internal interface ITextWorker : IDisposable
{
    string Model { get; }
    string Url { get; }
    string Key { get; }
    bool IsAlive { get; }
    long WorkingSetBytes { get; }
    Task StartAsync(CancellationToken ct);
}

// Requests capture their worker and cancellation token for the selected session.
internal sealed class TextModelHost : IDisposable
{
    private sealed class Entry
    {
        public readonly ITextWorker Worker;
        public Entry(ITextWorker worker) { Worker = worker; }
    }
    internal sealed class Lease : IDisposable
    {
        public ITextWorker Worker { get; }
        public CancellationToken SessionToken { get; }
        internal Lease(ITextWorker worker, CancellationToken token)
        { Worker = worker; SessionToken = token; }
        public void Dispose() { }
    }

    private readonly object gate = new();
    private readonly SemaphoreSlim starting = new(1);
    private readonly Func<string, ITextWorker> factory;
    private Entry? active, candidate;
    private CancellationTokenSource? session;
    private string mode = TextModels.Qwen35, status = "Models unloaded";
    private bool disposed;
    public event Action? StateChanged;

    internal TextModelHost(Func<string, ITextWorker> factory) => this.factory = factory;
    public string Mode { get { lock (gate) return mode; } }
    public string Status { get { lock (gate) return status; } }
    public bool Ready { get { lock (gate) return active?.Worker.IsAlive == true; } }
    internal string? ActiveModel { get { lock (gate) return active?.Worker.Model; } }
    internal int WorkerCount { get { lock (gate) return (active != null ? 1 : 0) + (candidate != null ? 1 : 0); } }
    public long WorkingSetBytes { get { lock (gate) return AllEntries().Sum(e => e.Worker.WorkingSetBytes); } }

    public void SetMode(string? value)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            value = TextModels.Normalize(value);
            if (mode == value) return;
            StopLocked(); mode = value;
        }
        StateChanged?.Invoke();
    }

    public async Task EnsureReadyAsync(CancellationToken ct)
    {
        CancellationTokenSource captured;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (active != null && !active.Worker.IsAlive) StopLocked();
            captured = session ??= new();
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, captured.Token);
        await starting.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            Entry next;
            lock (gate)
            {
                linked.Token.ThrowIfCancellationRequested();
                if (active?.Worker.IsAlive == true) return;
                // A crashed primary is replaced only in this session. A stopped
                // session's late continuations can never reset a newer session.
                if (active != null) throw new InvalidOperationException("The text model stopped. Please retry.");
                var model = mode;
                next = new(factory(model)); candidate = next;
                status = "Loading " + model + "…";
            }
            StateChanged?.Invoke();
            await next.Worker.StartAsync(linked.Token).ConfigureAwait(false);
            lock (gate)
            {
                linked.Token.ThrowIfCancellationRequested();
                if (session != captured || candidate != next) throw new OperationCanceledException(linked.Token);
                active = next; candidate = null;
                status = "Using " + next.Worker.Model;
            }
            StateChanged?.Invoke();
        }
        catch
        {
            lock (gate) { if (session == captured) StopLocked(); }
            StateChanged?.Invoke();
            throw;
        }
        finally { starting.Release(); }
    }

    public Lease Acquire()
    {
        lock (gate)
        {
            if (active?.Worker.IsAlive != true || session == null)
                throw new InvalidOperationException("The text model is not ready. Please try dictation again.");
            return new(active.Worker, session.Token);
        }
    }

    private IEnumerable<Entry> AllEntries() => new[] { active, candidate }.Where(e => e != null).Cast<Entry>();
    private void StopLocked()
    {
        var old = session; session = null;
        old?.Cancel(); // Cancel requests and pending load before killing workers.
        foreach (var entry in AllEntries()) entry.Worker.Dispose();
        active = candidate = null; status = "Models unloaded";
        // Cancellation sources remain valid for leases/late load continuations;
        // their registrations are disposed by linked request sources on completion.
    }
    public void Stop() { lock (gate) StopLocked(); StateChanged?.Invoke(); }
    public void Dispose() { lock (gate) { if (disposed) return; disposed = true; StopLocked(); } }
}
