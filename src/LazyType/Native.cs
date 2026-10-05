using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace LazyType;

internal static class Native
{
    public const int WM_HOTKEY = 0x0312;
    public const int WH_MOUSE_LL = 14;
    public const int WM_MOUSEMOVE = 0x0200;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_ASYNCWINDOWPOS = 0x4000;

    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int cx, int cy);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr hObject);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr handle);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] public static extern bool ReleaseCapture();
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")] public static extern uint TimeBeginPeriod(uint uMilliseconds);
    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")] public static extern uint TimeEndPeriod(uint uMilliseconds);
    public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] public static extern IntPtr GetModuleHandle(string? lpModuleName);
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public UIntPtr extra; }

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
    public static bool SetClipboardText(string text)
    {
        for (var i = 0; i < 5; i++)
        {
            try
            {
                Clipboard.SetDataObject(text, copy: true, retryTimes: 5, retryDelay: 40);
                return true;
            }
            catch
            {
                Thread.Sleep(40);
            }
        }
        return false;
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
    private string? insertedText, documentSnapshot;
    private TextPatternRange? insertionRange;
    private IReadOnlyList<TextSpan>? markedSpans;
    private TextPatternRange?[]? markedRanges;
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
    public bool TryGetBounds(out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        try
        {
            if (protectedField || element == null || element.Current.IsOffscreen) return false;
            var rect = element.Current.BoundingRectangle;
            if (rect.IsEmpty || rect.Width < 20 || rect.Height < 12) return false;
            bounds = Rectangle.FromLTRB((int)rect.Left, (int)rect.Top, (int)rect.Right, (int)rect.Bottom);
            return true;
        }
        catch { return false; }
    }
    private TextPattern? TextPattern()
    {
        if (protectedField || element == null || element.Current.IsPassword) return null;
        return element.TryGetCurrentPattern(System.Windows.Automation.TextPattern.Pattern, out var pattern) ? (TextPattern)pattern : null;
    }
    // Never replace a whole field: retain a unique range for just the inserted dictation.
    // Unsupported editors still offer a copyable preview.
    public void RememberInsertion(string text)
    {
        insertedText = null; documentSnapshot = null; insertionRange = null; markedRanges = null;
        try
        {
            var pattern = TextPattern();
            var document = pattern?.DocumentRange.GetText(32001);
            if (document == null || document.Length > 32000 || !HasUniqueText(document, text)) return;
            insertedText = text; documentSnapshot = document;
        }
        catch { }
    }
    internal static bool HasUniqueText(string document, string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var index = document.IndexOf(text, StringComparison.Ordinal);
        return index >= 0 && document.IndexOf(text, index + 1, StringComparison.Ordinal) < 0;
    }
    public bool CanReplaceInsertion()
    {
        try { return insertedText != null && documentSnapshot != null && TextPattern()?.DocumentRange.GetText(32001) == documentSnapshot; }
        catch { return false; }
    }
    // True when both ends of the line hit-test to the field's window, so a marker never draws over another app.
    // Click-through layered windows, such as the marker itself, are skipped by hit-testing.
    public bool IsUncovered(Rectangle line)
    {
        var y = line.Top + line.Height / 2;
        var inset = Math.Min(4, line.Width / 2);
        return OwnsPoint(line.Left + inset, y) && OwnsPoint(line.Right - 1 - inset, y);
    }
    private bool OwnsPoint(int x, int y) => Window != IntPtr.Zero && Native.GetAncestor(Native.WindowFromPoint(new Native.POINT { X = x, Y = y }), 2 /* GA_ROOT */) == Window;
    // Screen bounds of each visible line of the dictation, clipped to the field. Empty once the field changes.
    public Rectangle[] InsertionLineBounds() => Bounds(null)?[0] ?? Array.Empty<Rectangle>();
    // Line bounds for each span of the dictation, in order. Null once the field changes; an entry is empty
    // when the editor cannot locate that span.
    public Rectangle[][]? SpanBounds(IReadOnlyList<TextSpan> spans) => Bounds(spans);
    private Rectangle[][]? Bounds(IReadOnlyList<TextSpan>? spans)
    {
        try
        {
            if (!CanReplaceInsertion()) return null;
            insertionRange ??= TextPattern()?.DocumentRange.FindText(insertedText!, false, false);
            if (insertionRange == null || insertionRange.GetText(-1) != insertedText) { insertionRange = null; markedRanges = null; return null; }
            if (spans != null && (markedRanges == null || !ReferenceEquals(spans, markedSpans)))
            {
                markedSpans = spans;
                markedRanges = spans.Select(SpanRange).ToArray();
            }
            var ranges = spans == null ? new TextPatternRange?[] { insertionRange } : markedRanges!;
            var field = element!.Current.BoundingRectangle;
            return ranges.Select(range => range == null ? Array.Empty<Rectangle>() : ClipLines(range.GetBoundingRectangles(), field)).ToArray();
        }
        catch { insertionRange = null; markedRanges = null; return null; }
    }
    // Screen bounds of every visible line of text in the field, so labels can avoid covering neighbouring lines.
    public Rectangle[] TextLineBounds()
    {
        try
        {
            var pattern = TextPattern();
            return pattern == null ? Array.Empty<Rectangle>() : ClipLines(pattern.DocumentRange.GetBoundingRectangles(), element!.Current.BoundingRectangle);
        }
        catch { return Array.Empty<Rectangle>(); }
    }
    private static Rectangle[] ClipLines(IEnumerable<System.Windows.Rect> lines, System.Windows.Rect field) => lines
        .Select(line => System.Windows.Rect.Intersect(line, field))
        .Where(line => !line.IsEmpty && line.Width >= 1 && line.Height >= 4)
        .Select(line => Rectangle.FromLTRB((int)Math.Floor(line.Left), (int)Math.Floor(line.Top), (int)Math.Ceiling(line.Right), (int)Math.Ceiling(line.Bottom)))
        .ToArray();
    // A range for part of the dictation, used only if the editor reports exactly the expected text.
    private TextPatternRange? SpanRange(TextSpan span)
    {
        if (span.Start < 0 || span.Length <= 0 || span.Start + span.Length > insertedText!.Length) return null;
        var range = insertionRange!.Clone();
        range.MoveEndpointByRange(TextPatternRangeEndpoint.End, range, TextPatternRangeEndpoint.Start);
        if (range.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Character, span.Start + span.Length) != span.Start + span.Length) return null;
        if (range.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, span.Start) != span.Start) return null;
        return range.GetText(-1) == insertedText.Substring(span.Start, span.Length) ? range : null;
    }
    public async Task<bool> ReplaceInsertionAsync(string text, CancellationToken ct)
    {
        if (!CanReplaceInsertion()) return false;
        for (var i = 0; i < 40 && Native.ModifiersDown; i++) await Task.Delay(25, ct);
        ct.ThrowIfCancellationRequested();
        try
        {
            if (Native.ModifiersDown || !Native.SetForegroundWindow(Window)) return false;
            element!.SetFocus();
            if (!IsCurrent() || !CanReplaceInsertion()) return false;
            var pattern = TextPattern()!;
            var range = pattern.DocumentRange.FindText(insertedText!, false, false);
            if (range == null || range.GetText(-1) != insertedText) return false;
            range.Select();
            bool SelectionUnchanged()
            {
                try
                {
                    var selected = pattern.GetSelection();
                    // Chrome can report a different end position for the same text, so check the start and the exact selected text.
                    return CanReplaceInsertion() && selected.Length == 1
                        && selected[0].CompareEndpoints(TextPatternRangeEndpoint.Start, range, TextPatternRangeEndpoint.Start) == 0
                        && selected[0].GetText(insertedText!.Length + 1) == insertedText;
                }
                catch { return false; }
            }
            return await InsertAsync(text, ct, SelectionUnchanged);
        }
        catch (OperationCanceledException) { throw; }
        catch { return false; }
    }
    public async Task<bool> InsertAsync(string text, CancellationToken ct, Func<bool>? validateSelection = null)
    {
        for (var i = 0; i < 40 && Native.ModifiersDown; i++) await Task.Delay(25, ct);
        ct.ThrowIfCancellationRequested();
        if (Native.ModifiersDown || !IsCurrent()) return false;
        if (!Native.SetClipboardText(text)) return false;
        ct.ThrowIfCancellationRequested();
        if (!IsCurrent() || validateSelection?.Invoke() == false) return false;
        Native.Paste();
        await Task.Delay(50);
        return true;
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
