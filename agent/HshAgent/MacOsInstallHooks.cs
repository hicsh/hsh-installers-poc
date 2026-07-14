using System.Diagnostics;
using System.Linq;
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

        // --background lets Program.cs tell a LaunchAgent-managed launch (this
        // plist) apart from a human double-clicking the app in Finder, so it
        // knows whether to run headless or show the status/uninstall dialog.
        var plist = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
              <key>Label</key><string>{LaunchdLabel}</string>
              <key>ProgramArguments</key><array><string>{exe}</string><string>--background</string></array>
              <key>RunAtLoad</key><true/>
              <!-- Crash-only keep-alive: a plain <true/> restarts the job even
                   after a clean exit, so a port-conflict exit(0) or an update
                   handoff would loop forever. -->
              <key>KeepAlive</key>
              <dict><key>SuccessfulExit</key><false/></dict>
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

    /// <summary>Entry point for a manual (non-LaunchAgent) launch — e.g. double-clicking the
    /// app in Finder. Checks whether the background copy is actually reachable and offers to
    /// (re)start it, or offers to uninstall.</summary>
    public static void ShowManualLaunchDialog(int port)
    {
        if (!IsBackgroundAgentRunning(port))
        {
            var start = ShowDialog(
                "HSH Agent isn't currently running.",
                new[] { "Cancel", "Start" }, defaultButton: "Start");
            if (start == "Start")
            {
                RegisterLaunchAgent(); // idempotent: rewrites the plist (now with --background) and (re)loads it
                ShowDialog("HSH Agent is now running in the background.", new[] { "OK" }, defaultButton: "OK");
            }
            return;
        }

        var choice = ShowDialog(
            "HSH Agent is running in the background. It starts automatically when you log in.",
            new[] { "OK", "Uninstall…" }, defaultButton: "OK");
        if (choice != "Uninstall…") return;

        var confirmed = ShowDialog(
            "This removes HSH Agent, stops it from starting automatically, and deletes its cached data. This cannot be undone.",
            new[] { "Cancel", "Uninstall" }, defaultButton: "Cancel", caution: true);
        if (confirmed != "Uninstall") return;

        Uninstall();
    }

    private static bool IsBackgroundAgentRunning(int port)
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            var connect = client.BeginConnect("127.0.0.1", port, null, null);
            var ok = connect.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(300));
            return ok && client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private static void Uninstall()
    {
        // Stop autostart and kill the running background copy first, so nothing
        // fights the deletion below or relaunches it via KeepAlive mid-uninstall.
        // Unload by path (matching RegisterLaunchAgent's own style) rather than
        // `bootout gui/<uid>` — the uid must be numeric, and Environment.UserName
        // is the account name, not the numeric id, so that form silently no-ops.
        // Must run before the plist is deleted: launchctl reads it to find the
        // job's label.
        Run("/bin/launchctl", $"unload {UserLaunchAgentPlist}"); // best-effort
        try { File.Delete(UserLaunchAgentPlist); } catch { /* already gone */ }
        // Belt-and-suspenders in case unload didn't actually stop it (e.g. the
        // job wasn't tracked, or it's racing a KeepAlive restart).
        Run("/usr/bin/pkill", "-f \"HSH Agent.app/Contents/MacOS/HshAgent\"");

        // Ask Velopack where this running copy actually lives rather than guessing
        // between /Applications and ~/Applications — the locator already resolved
        // this at startup (VelopackApp.Run() in Program.cs).
        var appDir = Velopack.Locators.VelopackLocator.IsCurrentSet
            ? Velopack.Locators.VelopackLocator.Current.RootAppDir
            : null;

        if (!string.IsNullOrEmpty(appDir) && Directory.Exists(appDir))
        {
            // The pkg receipt DB is root-owned regardless of install location, so
            // `pkgutil --forget` always needs elevation even if the app itself
            // doesn't (a "just me" install can still leave a root-owned receipt).
            if (!IsWritable(appDir))
            {
                var cmd = $"rm -rf '{appDir}'; pkgutil --forget {LaunchdLabel} >/dev/null 2>&1 || true";
                if (!RunElevated(cmd, prompt: "HSH Agent needs your password to finish uninstalling."))
                {
                    ShowDialog(
                        "Uninstall cancelled. HSH Agent won't start automatically anymore, but the app is still in Applications — you can try again or delete it manually.",
                        new[] { "OK" }, defaultButton: "OK");
                    return;
                }
            }
            else
            {
                try { Directory.Delete(appDir, recursive: true); } catch { }
                Run("/usr/sbin/pkgutil", $"--forget {LaunchdLabel}"); // best-effort
            }
        }

        // Unprivileged cleanup.
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        try { Directory.Delete(Path.Combine(home, "Library", "Caches", "velopack", "HshAgent"), recursive: true); } catch { }
        try { File.Delete(Path.Combine(home, "Library", "Logs", "velopack_HshAgent.log")); } catch { }

        ShowDialog("HSH Agent has been uninstalled.", new[] { "OK" }, defaultButton: "OK");
        Environment.Exit(0);
    }

    private static bool IsWritable(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, $".hsh-write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Shows a native dialog via osascript and returns the clicked button, or null if
    /// cancelled/dismissed/unavailable (e.g. no GUI session). Never throws.</summary>
    private static string? ShowDialog(string message, string[] buttons, string defaultButton, bool caution = false)
    {
        var buttonList = string.Join(",", buttons.Select(b => $"\"{EscapeForAppleScript(b)}\""));
        var script = $"display dialog \"{EscapeForAppleScript(message)}\" with title \"HSH Agent\" " +
                     $"buttons {{{buttonList}}} default button \"{EscapeForAppleScript(defaultButton)}\"" +
                     (caution ? " with icon caution" : "");
        try
        {
            using var p = Process.Start(new ProcessStartInfo("/usr/bin/osascript")
            {
                ArgumentList = { "-e", script },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            });
            var output = p!.StandardOutput.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0) return null; // cancelled (AppleScript error -128) or no GUI session
            const string marker = "button returned:";
            var ix = output.IndexOf(marker, StringComparison.Ordinal);
            return ix < 0 ? null : output[(ix + marker.Length)..].Trim();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not show dialog: {ex.Message}");
            return null;
        }
    }

    /// <summary>Runs a shell command with an admin-privileges prompt via osascript. Returns false
    /// if the user cancelled the prompt or it otherwise failed. Never throws.</summary>
    private static bool RunElevated(string shellCommand, string prompt)
    {
        var script = $"do shell script \"{EscapeForAppleScript(shellCommand)}\" " +
                     $"with administrator privileges with prompt \"{EscapeForAppleScript(prompt)}\"";
        try
        {
            using var p = Process.Start(new ProcessStartInfo("/usr/bin/osascript")
            {
                ArgumentList = { "-e", script },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            });
            p!.WaitForExit();
            return p.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Elevated step failed: {ex.Message}");
            return false;
        }
    }

    private static string EscapeForAppleScript(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
