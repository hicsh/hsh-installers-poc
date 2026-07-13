using System.Diagnostics;
using System.Runtime.InteropServices;
using Velopack;

namespace HshAgent;

/// <summary>
/// Install/uninstall-time work, run from Velopack's hooks:
///
///  - Windows: <see cref="AfterInstall"/> / <see cref="BeforeUninstall"/> are
///    Velopack "fast callbacks", invoked elevated by Setup.exe / Update.exe.
///    They (un)register the Windows Service.
///  - macOS / Linux: there's no elevated fast callback, so the per-user autostart
///    entry (launchd LaunchAgent / systemd user unit) is registered from
///    <see cref="FirstRun"/> the first time the app is launched after install.
///
/// Keeping the agent registered with the OS service manager is what lets the
/// self-update flow simply exit the process and have it relaunch on the new
/// version.
/// </summary>
public static class InstallHooks
{
    private const string ServiceName = "HshAgent";
    private const string DisplayName = "HSH Agent";
    private const string LaunchdLabel = "com.hsh.agent";
    private const string SystemdUnit = "hsh-agent";

    /// <summary>
    /// Set during <see cref="FirstRun"/> when a managed copy of the agent is
    /// already running under the OS service manager by the time this hook
    /// fires: macOS/Linux register + start their autostart entry here (LaunchAgent
    /// <c>RunAtLoad</c> / systemd <c>--now</c>); on Windows, <see cref="AfterInstall"/>
    /// already created + started the Windows Service, elevated, earlier in the
    /// install. Either way, the first-run foreground process — launched by
    /// Setup.exe's "run app after install" — must exit instead of also starting
    /// the web host, otherwise both instances race to bind the HTTP port and the
    /// loser crashes, which under KeepAlive / sc.exe's restart policy turns into
    /// a relaunch/crash loop. See Program.cs.
    /// </summary>
    public static bool RegisteredSelfStartingAutostart { get; private set; }

    private static string UserLaunchAgentPlist =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "LaunchAgents", $"{LaunchdLabel}.plist");

    private static string UserSystemdUnit =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "systemd", "user", $"{SystemdUnit}.service");

    // ---- Windows: elevated, during (un)install -----------------------------

    public static void AfterInstall(SemanticVersion version)
    {
        RegisterWindowsService();
    }

    public static void BeforeUninstall(SemanticVersion version)
    {
        Run("sc.exe", $"stop {ServiceName}");
        Run("sc.exe", $"delete {ServiceName}");
    }

    private static void RegisterWindowsService()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return;
        // Auto-start service with a restart-on-failure recovery policy so a crash
        // (or a self-update exit) brings it straight back.
        Run("sc.exe", $"create {ServiceName} binPath= \"{exe}\" start= auto DisplayName= \"{DisplayName}\"");
        Run("sc.exe", $"failure {ServiceName} reset= 60 actions= restart/5000/restart/10000/restart/30000");
        Run("sc.exe", $"start {ServiceName}");
    }

    // ---- macOS / Linux: first launch after install -------------------------

    public static void FirstRun(SemanticVersion version)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            RegisterLaunchAgent();
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            RegisterSystemdUnit();
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            // AfterInstall (elevated fast callback) already created + started the
            // Windows Service earlier in the install. This foreground copy just
            // needs to hand off to it, same as the macOS/Linux branches above.
            RegisteredSelfStartingAutostart = true;

        Console.WriteLine($"HSH Agent v{version} installed.");
    }

    private static void RegisterLaunchAgent()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return;

        // Per-user LaunchAgent (no root): RunAtLoad + KeepAlive so it starts at
        // login and restarts on crash / after a self-update exits the process.
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

    private static void RegisterSystemdUnit()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return;

        // Per-user systemd unit: WantedBy default.target so it starts at login,
        // Restart=always so it comes back after a crash / self-update exit.
        var unit = $"""
            [Unit]
            Description=HSH Agent
            After=network.target

            [Service]
            ExecStart={exe}
            Restart=always
            RestartSec=3

            [Install]
            WantedBy=default.target
            """;
        try
        {
            var path = UserSystemdUnit;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, unit);
            Run("systemctl", "--user daemon-reload");
            Run("systemctl", $"--user enable --now {SystemdUnit}.service");   // --now starts it
            RegisteredSelfStartingAutostart = true;
            Console.WriteLine("Registered the HSH Agent as a systemd user service.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to register systemd unit: {ex.Message}");
        }
    }

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
