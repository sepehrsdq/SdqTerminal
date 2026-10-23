using System.Diagnostics;
using System.IO;
using System.Text;

namespace SdqTerminal;

internal static class TerminalDirectory
{
    public static string? FromReport(string? value, string? distribution)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl)) return null;
        if (value.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !uri.IsFile) return null;
            value = distribution is not null && !(uri.AbsolutePath.Length > 3 && uri.AbsolutePath[2] == ':')
                ? Uri.UnescapeDataString(uri.AbsolutePath) : uri.LocalPath;
        }
        if (distribution is not null && value.StartsWith('/'))
            value = @"\\wsl.localhost\" + distribution + value.Replace('/', '\\');
        return Path.IsPathFullyQualified(value) && !value.Any(char.IsControl) ? value : null;
    }

    public static async Task<string?> ReadWslAsync(string sessionId, string? distribution)
    {
        // Identify this pane by a child-only environment marker, not by whichever
        // Linux shell happens to be newest. No command is pasted into its input.
        const string script = """
            marker="SDQTERMINAL_SESSION_ID=$1"
            best=; bestpid=2147483647; bestrank=2
            for p in /proc/[0-9]*; do
                [ -r "$p/environ" ] || continue
                tr '\000' '\n' < "$p/environ" 2>/dev/null | grep -Fqx -- "$marker" || continue
                stat=$(cat "$p/stat" 2>/dev/null) || continue
                stat=${stat##*) }; set -- $stat
                rank=1
                if [ "${5:-0}" != 0 ] && [ "${3:-0}" = "${6:-1}" ]; then rank=0; fi
                pid=${p##*/}
                if [ "$rank" -lt "$bestrank" ] || { [ "$rank" -eq "$bestrank" ] && [ "$pid" -lt "$bestpid" ]; }; then
                    cwd=$(readlink "$p/cwd" 2>/dev/null) || continue
                    best=$cwd; bestpid=$pid; bestrank=$rank
                fi
            done
            [ -n "$best" ] || exit 3
            wslpath -w "$best"
            """;
        var info = new ProcessStartInfo(WslProfiles.Executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        info.Environment.Remove(TerminalEnvironment.SessionVariable);
        if (distribution is not null) { info.ArgumentList.Add("-d"); info.ArgumentList.Add(distribution); }
        foreach (var arg in new[] { "--exec", "sh", "-c", script, "rtl-directory", sessionId }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new IOException("Could not read the WSL directory.");
        var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch (InvalidOperationException) { }
            throw new IOException("Reading the current WSL directory timed out.");
        }
        var path = (await output).TrimEnd('\r', '\n'); await errors;
        return process.ExitCode == 0 ? FromReport(path, null) : null;
    }
}
