using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace LazyType;

internal static class Native
{
    public const int WM_HOTKEY = 0x0312;
    [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr handle);
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public INPUTUNION u; }
    [StructLayout(LayoutKind.Explicit)] private struct INPUTUNION
    {
        [FieldOffset(0)] public KEYBDINPUT keyboard;
        [FieldOffset(0)] public MOUSEINPUT mouse;
    }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort key, scan; public uint flags, time; public UIntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int x, y; public uint mouseData, flags, time; public UIntPtr extra; }
    private static INPUT Key(ushort key, bool up) => new() { type = 1, u = new() { keyboard = new() { key = key, flags = up ? 2u : 0u } } };
    public static void Paste()
    {
        var keys = new[] { Key(0x11, false), Key(0x56, false), Key(0x56, true), Key(0x11, true) };
        if (SendInput(4, keys, Marshal.SizeOf<INPUT>()) != 4) throw new InvalidOperationException("Windows blocked text insertion. Copy the result below and paste it manually.");
    }
    public static bool ModifiersDown => new[] { 0x10, 0x11, 0x12 }.Any(k => (GetAsyncKeyState(k) & 0x8000) != 0);
    public static Icon MakeIcon(Color color)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 1, 1, 30, 30);
            using var pen = new Pen(Color.White, 2.5f);
            g.DrawLine(pen, 16, 8, 16, 17); g.DrawArc(pen, 10, 10, 12, 13, 0, 180);
            g.DrawLine(pen, 16, 23, 16, 27);
        }
        var handle = bmp.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); }
        finally { DestroyIcon(handle); }
    }
}

internal sealed class TextTarget
{
    public IntPtr Window { get; private set; }
    private AutomationElement? element;
    private int[]? runtimeId;
    private bool protectedField;
    public static TextTarget Capture()
    {
        var target = new TextTarget { Window = Native.GetForegroundWindow() };
        try
        {
            target.element = AutomationElement.FocusedElement;
            target.runtimeId = target.element?.GetRuntimeId();
            target.protectedField = target.element?.Current.IsPassword == true;
        }
        catch { }
        return target;
    }
    public bool IsCurrent()
    {
        if (protectedField || Window == IntPtr.Zero || Native.GetForegroundWindow() != Window) return false;
        Native.GetWindowThreadProcessId(Window, out var pid);
        if (pid == Environment.ProcessId) return false;
        try
        {
            var current = AutomationElement.FocusedElement;
            if (current?.Current.IsPassword == true) return false;
            if (runtimeId != null && (current == null || !runtimeId.SequenceEqual(current.GetRuntimeId()))) return false;
        }
        catch { return false; }
        return true;
    }
    public async Task<bool> InsertAsync(string text, CancellationToken ct)
    {
        for (var i = 0; i < 40 && Native.ModifiersDown; i++) await Task.Delay(25, ct);
        ct.ThrowIfCancellationRequested();
        if (Native.ModifiersDown || !IsCurrent()) return false;
        IDataObject? previous;
        try { previous = Clipboard.GetDataObject(); }
        catch { return false; }
        uint ownedSequence = 0;
        try
        {
            Clipboard.SetText(text);
            ownedSequence = Native.GetClipboardSequenceNumber();
            ct.ThrowIfCancellationRequested();
            if (!IsCurrent()) return false;
            Native.Paste();
            // Keep clipboard available long enough for rich editors to consume it.
            await Task.Delay(1200);
            return true;
        }
        finally
        {
            // Never overwrite something the user copied while we were pasting.
            if (ownedSequence != 0 && Native.GetClipboardSequenceNumber() == ownedSequence)
                try { if (previous != null) Clipboard.SetDataObject(previous, true); else Clipboard.Clear(); } catch { }
        }
    }
}

internal sealed class ProcessJob : IDisposable
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateJobObject(IntPtr attrs, string? name);
    [DllImport("kernel32.dll")] private static extern bool SetInformationJobObject(IntPtr job, int type, IntPtr info, uint size);
    [DllImport("kernel32.dll")] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [StructLayout(LayoutKind.Sequential)] private struct Basic { public long perProcess, perJob; public uint flags; public UIntPtr min, max; public uint active; public UIntPtr affinity; public uint priority, scheduling; }
    [StructLayout(LayoutKind.Sequential)] private struct IO { public ulong a,b,c,d,e,f; }
    [StructLayout(LayoutKind.Sequential)] private struct Extended { public Basic basic; public IO io; public UIntPtr a,b,c,d; }
    private IntPtr handle;
    public ProcessJob()
    {
        handle = CreateJobObject(IntPtr.Zero, null);
        var limits = new Extended { basic = new Basic { flags = 0x2000 } };
        var size = Marshal.SizeOf<Extended>(); var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(limits, ptr, false);
            if (handle == IntPtr.Zero || !SetInformationJobObject(handle, 9, ptr, (uint)size)) throw new InvalidOperationException("Could not create model process lifetime guard.");
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }
    public void Add(Process p) { if (!AssignProcessToJobObject(handle, p.Handle)) { p.Kill(true); throw new InvalidOperationException("Could not attach model process lifetime guard."); } }
    public void Dispose() { if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; } }
}
