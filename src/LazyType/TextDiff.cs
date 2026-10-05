using System.Text.RegularExpressions;

namespace LazyType;

internal enum DiffKind { Equal, Removed, Added }
internal readonly record struct DiffPart(DiffKind Kind, string Text);
internal readonly record struct TextSpan(int Start, int Length);

// Word-level comparison of the original and suggested wording, so only what a suggestion changes is marked.
internal static class TextDiff
{
    private static readonly Regex Tokens = new(@"[\p{L}\p{N}]+(?:['’][\p{L}\p{N}]+)*|\s+|.", RegexOptions.Compiled);
    private static readonly Regex Words = new(@"[\p{L}\p{N}]+(?:['’][\p{L}\p{N}]+)*", RegexOptions.Compiled);

    public static List<DiffPart> Compare(string original, string suggestion)
    {
        var a = Split(original);
        var b = Split(suggestion);
        // Trim the shared start and end so the comparison table only covers the edited middle.
        var prefix = 0;
        while (prefix < a.Length && prefix < b.Length && a[prefix] == b[prefix]) prefix++;
        var suffix = 0;
        while (suffix < a.Length - prefix && suffix < b.Length - prefix && a[^(suffix + 1)] == b[^(suffix + 1)]) suffix++;
        var x = a[prefix..(a.Length - suffix)];
        var y = b[prefix..(b.Length - suffix)];

        // Very long rewrites are shown as one replacement rather than allocating a huge table.
        if ((long)x.Length * y.Length > 2_000_000)
        {
            var whole = new List<Block>();
            if (prefix > 0) whole.Add(new Block(string.Concat(a[..prefix])));
            whole.Add(new Block(null, string.Concat(x), string.Concat(y)));
            if (suffix > 0) whole.Add(new Block(string.Concat(a[^suffix..])));
            return ToParts(whole);
        }

        var lengths = new int[x.Length + 1, y.Length + 1];
        for (var i = x.Length - 1; i >= 0; i--)
            for (var j = y.Length - 1; j >= 0; j--)
                lengths[i, j] = x[i] == y[j] ? lengths[i + 1, j + 1] + 1 : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);

        var blocks = new List<Block>();
        if (prefix > 0) blocks.Add(new Block(string.Concat(a[..prefix])));
        int p = 0, q = 0;
        while (p < x.Length || q < y.Length)
        {
            if (p < x.Length && q < y.Length && x[p] == y[q]) { Append(blocks, x[p], null, null); p++; q++; }
            else if (q >= y.Length || (p < x.Length && lengths[p + 1, q] >= lengths[p, q + 1])) { Append(blocks, null, x[p], null); p++; }
            else { Append(blocks, null, null, y[q]); q++; }
        }
        if (suffix > 0) Append(blocks, string.Concat(a[^suffix..]), null, null);

        // Fold spaces and punctuation sandwiched between two edits into one edit, so a rewritten phrase reads as a whole.
        for (var i = 1; i < blocks.Count - 1; i++)
        {
            if (blocks[i].Equal is not { } gap || Words.IsMatch(gap) || blocks[i - 1].Equal != null || blocks[i + 1].Equal != null) continue;
            blocks[i - 1] = new Block(null, blocks[i - 1].Removed + gap + blocks[i + 1].Removed, blocks[i - 1].Added + gap + blocks[i + 1].Added);
            blocks.RemoveRange(i, 2);
            i--;
        }
        return ToParts(blocks);
    }

    private static List<DiffPart> ToParts(List<Block> blocks)
    {
        var parts = new List<DiffPart>();
        foreach (var block in blocks)
        {
            if (block.Equal != null) parts.Add(new DiffPart(DiffKind.Equal, block.Equal));
            if (!string.IsNullOrEmpty(block.Removed)) parts.Add(new DiffPart(DiffKind.Removed, block.Removed));
            if (!string.IsNullOrEmpty(block.Added)) parts.Add(new DiffPart(DiffKind.Added, block.Added));
        }
        return parts;
    }

    // Whitespace-only differences are not worth applying.
    public static bool HasChanges(IEnumerable<DiffPart> parts) => parts.Any(part => part.Kind != DiffKind.Equal && !string.IsNullOrWhiteSpace(part.Text));

    // Ranges of the original text that the suggestion edits. A pure insertion marks the word just before it.
    public static List<TextSpan> ChangedSpans(string original, IReadOnlyList<DiffPart> parts)
    {
        var spans = new List<TextSpan>();
        var offset = 0;
        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            if (part.Kind == DiffKind.Added)
            {
                var replaced = i > 0 && parts[i - 1].Kind == DiffKind.Removed;
                if (!replaced && !string.IsNullOrWhiteSpace(part.Text) && NearestWord(original, offset) is { } word) spans.Add(word);
                continue;
            }
            if (part.Kind == DiffKind.Removed && !string.IsNullOrWhiteSpace(part.Text))
            {
                var start = offset + (part.Text.Length - part.Text.TrimStart().Length);
                spans.Add(new TextSpan(start, part.Text.Trim().Length));
            }
            offset += part.Text.Length;
        }
        var merged = new List<TextSpan>();
        foreach (var span in spans.OrderBy(s => s.Start))
        {
            if (merged.Count > 0 && span.Start <= merged[^1].Start + merged[^1].Length)
            {
                var last = merged[^1];
                merged[^1] = new TextSpan(last.Start, Math.Max(last.Start + last.Length, span.Start + span.Length) - last.Start);
            }
            else merged.Add(span);
        }
        return merged;
    }

    private static TextSpan? NearestWord(string text, int position)
    {
        Match? before = null, after = null;
        foreach (Match match in Words.Matches(text))
        {
            if (match.Index + match.Length <= position) before = match;
            else { after ??= match; break; }
        }
        var word = before ?? after;
        return word == null ? null : new TextSpan(word.Index, word.Length);
    }

    private static string[] Split(string text) => Tokens.Matches(text).Select(m => m.Value).ToArray();

    private static void Append(List<Block> blocks, string? equal, string? removed, string? added)
    {
        var last = blocks.Count > 0 ? blocks[^1] : null;
        if (equal != null)
        {
            if (last?.Equal != null) blocks[^1] = new Block(last.Equal + equal);
            else blocks.Add(new Block(equal));
        }
        else if (last != null && last.Equal == null) blocks[^1] = new Block(null, last.Removed + removed, last.Added + added);
        else blocks.Add(new Block(null, removed, added));
    }

    private sealed record Block(string? Equal, string? Removed = null, string? Added = null);
}
