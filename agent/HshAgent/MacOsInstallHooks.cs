using System.Diagnostics;
using Velopack;

namespace HshAgent;

public static class MacOsInstallHooks
{
    private const string LaunchdLabel = "com.hsh.agent";

    public static bool RegisteredSelfStartingAutostart { get; set; }

    public static void FirstRun(SemanticVersion version)
    {
        RegisterLaunchAgent();
        Console.WriteLine($"HSH Agent v{version} installed.");
    }

    private static void RegisterLaunchAgent()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return;

        var plist = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
              <key>Label</key><string>{LaunchdLabel}</string>
              <key>ProgramArguments</key><array><string>{exe}</string></array>
              <key>RunAtLoad</key><true/>
              <key>KeepAlive</key><true/>
            </dict>
            </plist>
            """;
        try
        {
            var path = UserLaunchAgentPlist;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, plist);
            Run("/bin/launchctl", $"unload {path}");   // ignore-if-absent
            Run("/bin/launchctl", $"load -w {path}");   // RunAtLoad starts it now
            RegisteredSelfStartingAutostart = true;
            Console.WriteLine("Registered the HSH Agent to start at login.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to register LaunchAgent: {ex.Message}");
        }
    }

    private static string UserLaunchAgentPlist =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "LaunchAgents", $"{LaunchdLabel}.plist");

    private static void Run(string file, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            p?.WaitForExit();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Install step failed ({file} {args}): {ex.Message}");
        }
    }
}
