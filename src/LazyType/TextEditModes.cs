namespace LazyType;

internal static class TextEditModes
{
    public const string Cleanup = "Clean up";
    public const string Reword = "Reword";
    public static string Normalize(string? mode) => mode == Reword ? Reword : Cleanup;
}
