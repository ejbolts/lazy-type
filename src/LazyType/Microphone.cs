using NAudio.Wave;

namespace LazyType;

internal sealed class Microphone : IDisposable
{
    private readonly WaveInEvent source;
    private readonly MemoryStream buffer = new();
    private readonly object gate = new();
    private readonly TaskCompletionSource<byte[]> stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int loudSamples;
    private bool ended;
    private bool disposed;
    public float Level { get; private set; }
    public bool HasSpeech => loudSamples >= 1600;
    public event Action? LimitReached;
    public Microphone(int device)
    {
        source = new WaveInEvent { DeviceNumber = device, WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 50 };
        source.DataAvailable += (_, e) =>
        {
            var reachedLimit = false;
            lock (gate)
            {
                if (ended || disposed) return;
                double sum = 0;
                for (var i = 0; i + 1 < e.BytesRecorded; i += 2)
                {
                    var value = BitConverter.ToInt16(e.Buffer, i) / 32768f;
                    sum += value * value;
                    if (Math.Abs(value) > 0.008) Interlocked.Increment(ref loudSamples);
                }
                Level = Math.Min(1f, (float)Math.Sqrt(sum / Math.Max(1, e.BytesRecorded / 2)) * 8);
                buffer.Write(e.Buffer, 0, e.BytesRecorded);
                if (buffer.Length >= 16000 * 2 * 120) { ended = true; reachedLimit = true; }
            }
            if (reachedLimit) LimitReached?.Invoke();
        };
        source.RecordingStopped += (_, e) =>
        {
            lock (gate)
            {
                // NAudio may deliver this callback after Escape has disposed capture.
                if (disposed) return;
                ended = true;
                if (e.Exception != null) stopped.TrySetException(e.Exception);
                else try
                {
                    using var output = new MemoryStream();
                    using (var writer = new WaveFileWriter(new NAudio.Utils.IgnoreDisposeStream(output), source.WaveFormat))
                        writer.Write(buffer.GetBuffer(), 0, (int)buffer.Length);
                    stopped.TrySetResult(output.ToArray());
                }
                catch (Exception error) { stopped.TrySetException(error); }
            }
        };
    }
    public void Start() => source.StartRecording();
    public async Task<byte[]> StopAsync()
    {
        source.StopRecording();
        try { return await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
        finally { Dispose(); }
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true; ended = true; Level = 0;
            stopped.TrySetCanceled(); buffer.Dispose();
        }
        source.Dispose();
    }
    public static List<(int Id, string Name)> Devices()
    {
        var devices = new List<(int, string)> { (-1, "Windows default microphone") };
        for (var i = 0; i < WaveIn.DeviceCount; i++) devices.Add((i, WaveIn.GetCapabilities(i).ProductName));
        return devices;
    }
}
