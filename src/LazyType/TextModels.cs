namespace LazyType;

internal static class TextModels
{
    public const string Current = "Qwen3 4B";
    public const string Qwen35 = "Qwen3.5 9B";
    public const string Gemma = "Gemma 4 12B";
    public const string Dynamic = "Dynamic";
    public static readonly string[] Choices = { Current, Qwen35, Gemma, Dynamic };
    public static string Normalize(string? choice) => Choices.Contains(choice) ? choice! : Current;

    public static string PathFor(string choice)
    {
        var (name, downloaded) = choice switch
        {
            Qwen35 => ("qwen3.5-9b-q4.gguf", "Qwen3.5-9B-Q4_K_M.gguf"),
            Gemma => ("gemma4-12b-q4.gguf", "gemma-4-12b-it-Q4_K_M.gguf"),
            _ => ("qwen3-4b-q4.gguf", "")
        };
        var installed = Path.Combine(AppSettings.Root, "models", name);
        if (File.Exists(installed) || downloaded.Length == 0) return installed;
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

// Each request holds a lease on its exact worker. Promotion changes only which
// worker NEW requests acquire; an old worker drains before it is terminated.
internal sealed class TextModelHost : IDisposable
{
    private sealed class Entry
    {
        public readonly ITextWorker Worker;
        public int Readers;
        public bool Retired;
        public Entry(ITextWorker worker) { Worker = worker; }
    }
    internal sealed class Lease : IDisposable
    {
        private Action? release;
        public ITextWorker Worker { get; }
        public CancellationToken SessionToken { get; }
        internal Lease(ITextWorker worker, CancellationToken token, Action release)
        { Worker = worker; SessionToken = token; this.release = release; }
        public void Dispose() => Interlocked.Exchange(ref release, null)?.Invoke();
    }

    private readonly object gate = new();
    private readonly SemaphoreSlim starting = new(1);
    private readonly Func<string, ITextWorker> factory;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    private readonly List<Entry> retired = new();
    private Entry? active, candidate;
    private CancellationTokenSource? session;
    private Task upgrade = Task.CompletedTask;
    private string mode = TextModels.Current, status = "Models unloaded";
    private bool disposed;
    public event Action? StateChanged;

    internal TextModelHost(Func<string, ITextWorker> factory, Func<TimeSpan, CancellationToken, Task>? delay = null)
    { this.factory = factory; this.delay = delay ?? Task.Delay; }
    public string Mode { get { lock (gate) return mode; } }
    public string Status { get { lock (gate) return status; } }
    public bool Ready { get { lock (gate) return active?.Worker.IsAlive == true; } }
    internal Task UpgradeTask { get { lock (gate) return upgrade; } }
    internal string? ActiveModel { get { lock (gate) return active?.Worker.Model; } }
    internal int WorkerCount { get { lock (gate) return (active != null ? 1 : 0) + (candidate != null ? 1 : 0) + retired.Count; } }
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
                var model = mode == TextModels.Dynamic ? TextModels.Current : mode;
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
                if (mode == TextModels.Dynamic)
                {
                    status += " · Gemma loads after 5 seconds";
                    upgrade = Task.Run(() => UpgradeAsync(captured));
                }
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

    private async Task UpgradeAsync(CancellationTokenSource captured)
    {
        Entry? next = null;
        try
        {
            await delay(TimeSpan.FromSeconds(5), captured.Token).ConfigureAwait(false);
            lock (gate)
            {
                captured.Token.ThrowIfCancellationRequested();
                if (session != captured || mode != TextModels.Dynamic) return;
                if (active?.Worker.IsAlive != true) throw new InvalidOperationException("The current model stopped.");
                next = new(factory(TextModels.Gemma)); candidate = next;
                status = "Using " + active?.Worker.Model + " · loading Gemma…";
            }
            StateChanged?.Invoke();
            await next.Worker.StartAsync(captured.Token).ConfigureAwait(false);
            lock (gate)
            {
                captured.Token.ThrowIfCancellationRequested();
                if (session != captured || candidate != next) return;
                var previous = active;
                active = next; candidate = null;
                if (previous != null)
                {
                    previous.Retired = true;
                    if (previous.Readers == 0) previous.Worker.Dispose();
                    else retired.Add(previous);
                }
                status = "Using " + TextModels.Gemma;
            }
            AppLog.Write("Dynamic model handover complete; new edits use Gemma.");
            StateChanged?.Invoke();
        }
        catch (Exception e)
        {
            lock (gate)
            {
                if (session != captured) return;
                if (candidate == next) candidate = null;
                next?.Worker.Dispose();
                if (e is not OperationCanceledException)
                    status = "Using " + active?.Worker.Model + " · Gemma unavailable";
            }
            if (e is not OperationCanceledException)
                AppLog.Write("Dynamic Gemma load failed; current model retained (" + e.GetType().Name + ").");
            StateChanged?.Invoke();
        }
    }

    public Lease Acquire()
    {
        lock (gate)
        {
            if (active?.Worker.IsAlive != true || session == null)
                throw new InvalidOperationException("The text model is not ready. Please try dictation again.");
            var entry = active;
            entry.Readers++;
            return new(entry.Worker, session.Token, () =>
            {
                lock (gate)
                {
                    entry.Readers--;
                    if (entry.Retired && entry.Readers == 0)
                    { retired.Remove(entry); entry.Worker.Dispose(); }
                }
            });
        }
    }

    private IEnumerable<Entry> AllEntries() => new[] { active, candidate }.Where(e => e != null).Cast<Entry>().Concat(retired);
    private void StopLocked()
    {
        var old = session; session = null;
        old?.Cancel(); // Cancel requests and pending delay/load before killing workers.
        foreach (var entry in AllEntries()) entry.Worker.Dispose();
        active = candidate = null; retired.Clear(); status = "Models unloaded";
        // Cancellation sources remain valid for leases/late load continuations;
        // their registrations are disposed by linked request sources on completion.
    }
    public void Stop() { lock (gate) StopLocked(); StateChanged?.Invoke(); }
    public void Dispose() { lock (gate) { if (disposed) return; disposed = true; StopLocked(); } }
}
