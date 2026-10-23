using Microsoft.Win32;
using System;

namespace SdqTerminal;

public static class ContextMenuIntegration
{
    private const string SettingsKey = @"Software\SdqTerminal";
    private const string PromptValue = "ContextMenuPromptAnswered";
    private const string DirectoryShellKey =
        @"Software\Classes\Directory\shell\SdqTerminal";
    private const string BackgroundShellKey =
        @"Software\Classes\Directory\Background\shell\SdqTerminal";

    public static bool HasAnsweredInitialPrompt()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKey);
        return key?.GetValue(PromptValue) is int value && value == 1;
    }

    public static void MarkInitialPromptAnswered()
    {
        using var key = Registry.CurrentUser.CreateSubKey(SettingsKey);
        key.SetValue(PromptValue, 1, RegistryValueKind.DWord);
    }

    public static bool IsInstalled()
    {
        using var directoryKey = Registry.CurrentUser.OpenSubKey(DirectoryShellKey);
        using var backgroundKey = Registry.CurrentUser.OpenSubKey(BackgroundShellKey);
        return directoryKey is not null && backgroundKey is not null;
    }

    public static void Install()
    {
        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException(
                "مسیر فایل اجرایی برنامه پیدا نشد.");

        CreateShellEntry(DirectoryShellKey, executablePath, "%V");
        CreateShellEntry(BackgroundShellKey, executablePath, "%V");
    }

    public static void Uninstall()
    {
        Registry.CurrentUser.DeleteSubKeyTree(
            DirectoryShellKey,
            throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(
            BackgroundShellKey,
            throwOnMissingSubKey: false);
    }

    private static void CreateShellEntry(
        string keyPath,
        string executablePath,
        string pathPlaceholder)
    {
        using var shellKey = Registry.CurrentUser.CreateSubKey(keyPath);
        shellKey.SetValue(null, "Open in SdqTerminal");
        shellKey.SetValue("Icon", executablePath);

        using var commandKey = shellKey.CreateSubKey("command");
        commandKey.SetValue(
            null,
            $"\"{executablePath}\" \"{pathPlaceholder}\"");
    }
}
