using System.Runtime.Versioning;
using Microsoft.Win32;
using Velopack;

namespace HshAgent;

[SupportedOSPlatform("windows")]
public static class WindowsInstallHooks
{
    private const string RunValueName = "HshAgent";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private static readonly string LogFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HshAgent", "install.log");

    public static void AfterInstall(SemanticVersion version)
    {
        LogToFile("AfterInstall hook called");
        RegisterAutostart();
    }

    public static void FirstRun(SemanticVersion version)
    {
        LogToFile("FirstRun hook called");
        RegisterAutostart();
        Console.WriteLine($"HSH Agent v{version} installed.");
    }

    public static void BeforeUninstall(SemanticVersion version)
    {
        LogToFile("BeforeUninstall hook called");
        UnregisterAutostart();
    }

    // Registers the agent in the per-user Run key, the standard no-admin
    // autostart mechanism (the same one Slack/Discord use). Everything with
    // more machinery failed here: creating a Windows Service always needs an
    // elevated token, and even `schtasks /Create /SC ONLOGON /RL LIMITED` is
    // denied from a non-elevated process — while Velopack deliberately
    // installs to %LocalAppData% without elevation. Writing HKCU needs no
    // permissions at all. The value points at the Velopack `current` path,
    // which stays stable across updates.
    private static void RegisterAutostart()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            LogToFile("ERROR: ProcessPath is null or empty");
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            key.SetValue(RunValueName, $"\"{exe}\"");
            LogToFile($"Registered autostart Run key -> {exe}");
        }
        catch (Exception ex)
        {
            LogToFile($"Failed to register autostart Run key: {ex.Message}");
        }
    }

    private static void UnregisterAutostart()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(RunValueName, throwOnMissingValue: false);
            LogToFile("Removed autostart Run key");
        }
        catch (Exception ex)
        {
            LogToFile($"Failed to remove autostart Run key: {ex.Message}");
        }
    }

    private static void LogToFile(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);
            File.AppendAllText(LogFile, $"{DateTime.UtcNow:O} {message}\n");
        }
        catch { }
    }
}
