namespace SdqTerminal;

public static class ShellPath
{
    public const string PercentVariable = "SDQTERMINAL_LITERAL_PERCENT";
    public static void Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Any(char.IsControl) || path.Contains('"'))
            throw new ArgumentException("The file path contains unsupported control characters or quotes.");
    }
    public static string Windows(string path, bool powerShell, bool escapePercent = true)
    {
        Validate(path);
        return powerShell ? "'" + path.Replace("'", "''") + "'"
            : "\"" + (escapePercent ? path.Replace("%", "%" + PercentVariable + "%") : path) + "\"";
    }
    public static string WindowsArgument(string value)
    {
        // CommandLineToArgvW quoting for process arguments, not shell source.
        var result = new System.Text.StringBuilder("\"");
        var slashes = 0;
        foreach (var c in value)
        {
            if (c == '\\') { slashes++; continue; }
            result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes); slashes = 0;
            result.Append(c);
        }
        result.Append('\\', slashes * 2); return result.Append('"').ToString();
    }
}
