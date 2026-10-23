using System.IO;
using System.Security;
using Microsoft.Win32;

namespace SdqTerminal;

internal static class WslProfiles
{
    private const string LxssKey = @"Software\Microsoft\Windows\CurrentVersion\Lxss";
    public static string Executable => Path.Combine(Environment.SystemDirectory, "wsl.exe");
    public static bool IsAvailable => File.Exists(Executable);

    // Windows Terminal's WslDistroGenerator enumerates registered GUID keys rather
    // than parsing localized `wsl --list` output. Utility distributions are hidden.
    // https://github.com/microsoft/terminal/blob/main/src/cascadia/TerminalSettingsModel/WslDistroGenerator.cpp
    public static IReadOnlyList<string> Discover()
    {
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(LxssKey);
            return ReadDistributions(root);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
        { return []; }
    }

    internal static IReadOnlyList<string> ReadDistributions(RegistryKey? root)
    {
        if (root is null) return [];
        var names = new List<string>();
        foreach (var id in root.GetSubKeyNames())
        {
            if (!Guid.TryParseExact(id, "B", out _)) continue;
            try
            {
                using var entry = root.OpenSubKey(id);
                if (entry?.GetValue("DistributionName") is not string name ||
                    string.IsNullOrWhiteSpace(name) || name.Any(char.IsControl) || name.Any(char.IsWhiteSpace) || name.Contains('"') ||
                    name.StartsWith("docker-desktop", StringComparison.Ordinal) ||
                    name.StartsWith("rancher-desktop", StringComparison.Ordinal)) continue;
                // Modern distributions supply WT JSON fragments instead of legacy
                // generated profiles. We launch them directly by their registered
                // name as well, since this app does not consume WT settings fragments.
                names.Add(name);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
            { /* A distribution can disappear while the menu is being built. */ }
        }
        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static string? DefaultDistribution()
    {
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(LxssKey);
            return ReadDefaultDistribution(root);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
        { return null; }
    }

    internal static string? ReadDefaultDistribution(RegistryKey? root)
    {
        if (root?.GetValue("DefaultDistribution") is not string id || !Guid.TryParseExact(id, "B", out _)) return null;
        using var entry = root.OpenSubKey(id);
        return entry?.GetValue("DistributionName") is string name &&
            ReadDistributions(root).Contains(name, StringComparer.OrdinalIgnoreCase) ? name : null;
    }

    public static string CommandLine(string? distribution, string? directory = null)
    {
        // Let wsl.exe start the distribution's configured default shell. No sh/bash
        // wrapper, login-shell override, or startup script is injected.
        var command = ShellPath.WindowsArgument(Executable);
        if (!string.IsNullOrWhiteSpace(distribution))
        {
            // WSL's option parser preserves quotes around a distribution name
            // (microsoft/WSL#9792). WT appends the registered name without quotes.
            // Names cannot contain whitespace or quotes; do not reinterpret corrupt
            // registration data as extra wsl.exe options.
            if (distribution.Any(char.IsWhiteSpace) || distribution.Any(char.IsControl) || distribution.Contains('"'))
                throw new ArgumentException("The registered WSL distribution name is invalid.");
            command += " -d " + distribution;
        }
        // WT generated WSL profiles start in ~ on Windows build 19041 and newer.
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            command += directory is null ? " --cd ~" : " --cd " + ShellPath.WindowsArgument(directory);
        return command;
    }
}
