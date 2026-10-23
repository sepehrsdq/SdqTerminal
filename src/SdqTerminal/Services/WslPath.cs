using System.IO;

namespace SdqTerminal;

public static class WslPath
{
    // Only WSL share paths can be resolved without asking the target distribution.
    public static string? SharePath(string path, string distribution)
    {
        ShellPath.Validate(path);
        foreach (var prefix in new[] { @"\\wsl.localhost\", @"\\wsl$\" })
        {
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var rest = path[prefix.Length..]; var slash = rest.IndexOf('\\');
            var source = slash < 0 ? rest : rest[..slash];
            if (!string.Equals(source, distribution, StringComparison.OrdinalIgnoreCase))
                throw new IOException($"This file belongs to WSL distribution '{source}', but the target pane uses '{distribution}'.");
            return slash < 0 ? "/" : rest[slash..].Replace('\\', '/');
        }
        return null;
    }
    public static string Quote(string path)
    {
        ShellPath.Validate(path);
        return "'" + path.Replace("'", "'\\''") + "'";
    }
}
