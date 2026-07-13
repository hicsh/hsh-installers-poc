using System.Diagnostics;
using Velopack;

namespace HshAgent;

public static class WindowsInstallHooks
{
    private const string TaskName = "HshAgent";
    private static readonly string LogFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HshAgent", "install.log");

    public static bool RegisteredSelfStartingAutostart { get; set; }

    public static void AfterInstall(SemanticVersion version)
    {
        LogToFile("AfterInstall hook called");
        RegisterScheduledTask();
    }

    public static void BeforeUninstall(SemanticVersion version)
    {
        UnregisterScheduledTask();
    }

    public static void FirstRun(SemanticVersion version)
    {
        LogToFile("FirstRun hook called");

        try
        {
            RegisterScheduledTask();
            RegisteredSelfStartingAutostart = true;
        }
        catch (Exception ex)
        {
            LogToFile($"FirstRun: Failed to register scheduled task: {ex.Message}");
        }

        Console.WriteLine($"HSH Agent v{version} installed.");
    }

    // Registers a per-user logon Scheduled Task instead of a Windows Service:
    // creating a Win32 Service always requires an elevated token, but Velopack
    // installs to %LocalAppData% specifically so it never needs elevation, so
    // Win32_Service.Create reliably failed with access-denied (error code 2)
    // from both the AfterInstall and FirstRun hooks. A ONLOGON task created
    // with /RL LIMITED runs at the user's normal privilege level, matching how
    // the installer itself runs.
    private static void RegisterScheduledTask()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            LogToFile("ERROR: ProcessPath is null or empty");
            return;
        }

        LogToFile($"RegisterScheduledTask called with exe: {exe}");

        var createArgs = new[]
        {
            "/Create", "/TN", TaskName,
            "/TR", $"\"{exe}\"",
            "/SC", "ONLOGON",
            "/RL", "LIMITED",
            "/F",
        };
        var (createExit, createOutput) = Run("schtasks.exe", createArgs);
        if (createExit != 0)
        {
            LogToFile($"Failed to create scheduled task: exit code {createExit}, output: {createOutput}");
            return;
        }

        LogToFile("Scheduled task created, starting now...");
        var (runExit, runOutput) = Run("schtasks.exe", new[] { "/Run", "/TN", TaskName });
        if (runExit != 0)
        {
            LogToFile($"Failed to start scheduled task: exit code {runExit}, output: {runOutput}");
            return;
        }

        LogToFile("Scheduled task registered and started successfully.");
        Console.WriteLine("Scheduled task registered and started successfully.");
    }

    private static void UnregisterScheduledTask()
    {
        try
        {
            Run("schtasks.exe", new[] { "/Delete", "/TN", TaskName, "/F" });
            Console.WriteLine("Scheduled task unregistered successfully.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to unregister scheduled task: {ex.Message}");
        }
    }

    private static (int ExitCode, string Output) Run(string file, string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(file)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var arg in args) psi.ArgumentList.Add(arg);

            using var p = Process.Start(psi);
            if (p is null) return (-1, "process failed to start");

            var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit();
            return (p.ExitCode, output.Trim());
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
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
