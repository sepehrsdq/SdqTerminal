using System.Diagnostics;
using System.IO;

namespace SdqTerminal;

internal static class WslPathResolver
{
    public static async Task<string> ConvertAsync(string path, string? distribution)
    {
        if (string.IsNullOrWhiteSpace(distribution))
            throw new IOException("The WSL distribution for this pane could not be identified. Open a new WSL pane and try again.");
        var share = WslPath.SharePath(path, distribution);
        if (share is not null) return share;
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "wsl.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            RedirectStandardError = true, StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        foreach (var arg in new[] { "--distribution", distribution, "--exec", "wslpath", "-a", "-u", path }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new IOException("Could not start WSL path conversion.");
        var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new IOException("WSL path conversion timed out. Check that the distribution is running.");
        }
        var converted = (await output).TrimEnd('\r', '\n'); var error = await errors;
        if (process.ExitCode != 0 || !converted.StartsWith('/') || converted.Any(char.IsControl))
            throw new IOException("WSL could not access this path. " + error.Trim());
        return converted;
    }
}
