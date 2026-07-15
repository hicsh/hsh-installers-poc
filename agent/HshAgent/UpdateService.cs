using System.Reflection;
using System.Runtime.InteropServices;
using Velopack;
using Velopack.Sources;

namespace HshAgent;

/// <summary>
/// How the agent is allowed to update itself. Configured under "Update:Policy"
/// (see appsettings.json). Fleets managed centrally (Intune/Jamf) would set
/// <see cref="Disabled"/> and push signed installers themselves.
/// </summary>
public enum UpdatePolicy
{
    /// <summary>Scheduled background checks + apply, plus on-demand from the web app.</summary>
    Auto,

    /// <summary>No scheduled checks; only the web app's "Update now" button triggers an update.</summary>
    OnDemandOnly,

    /// <summary>In-app update turned off — updates are managed externally.</summary>
    Disabled,
}

/// <summary>The lifecycle of an update, surfaced to the web app via GET /update/status.</summary>
public enum UpdateState
{
    Idle,
    Checking,
    UpdateAvailable,
    Downloading,
    Ready,
    Applying,
    UpToDate,
    Failed,
    /// <summary>Policy is Disabled — the web app hides the button and shows a managed-install note.</summary>
    Disabled,
    /// <summary>Not running as a Velopack install (dev build) — self-update isn't possible; the web app falls back to the download link.</summary>
    Unsupported,
}

/// <summary>Flat snapshot serialized to the web app. <c>supported</c> tells the UI whether to offer self-update or fall back to a download link.</summary>
public sealed record UpdateStatusDto(
    string State,
    string Policy,
    bool Supported,
    string Installed,
    string? Latest,
    int? Percent,
    string? Error);

/// <summary>
/// Wraps Velopack's <see cref="UpdateManager"/> as the agent's self-update engine,
/// reading releases from the project's GitHub Releases (<see cref="GithubSource"/>).
/// Runs the optional scheduled background check (policy Auto) and serves the
/// on-demand update triggered by the web app's "Update now" button.
///
/// Applies via <see cref="UpdateManager.WaitExitThenApplyUpdates(VelopackAsset, bool, bool, string[])"/>
/// so Update.exe relaunches the freshly-installed version itself. Neither
/// autostart mechanism would do that for us: the Windows HKCU Run key has no
/// keep-alive, and the macOS LaunchAgent only restarts the agent on a crash.
/// </summary>
public sealed class UpdateService : BackgroundService
{
    private readonly ILogger<UpdateService> _log;
    private readonly UpdatePolicy _policy;
    private readonly TimeSpan _checkInterval;
    private readonly UpdateManager? _mgr;
    private readonly string _installedVersion;

    private readonly object _gate = new();
    private UpdateState _state;
    private string? _latest;
    private int? _percent;
    private string? _error;
    private UpdateInfo? _pending;
    private Task? _activeOp;

    public UpdateService(IConfiguration config, ILogger<UpdateService> log)
    {
        _log = log;

        var section = config.GetSection("Update");
        _policy = Enum.TryParse<UpdatePolicy>(section["Policy"], ignoreCase: true, out var p)
            ? p
            : UpdatePolicy.OnDemandOnly;
        _checkInterval = TimeSpan.FromHours(
            double.TryParse(section["CheckIntervalHours"], out var h) && h > 0 ? h : 6);

        _installedVersion = typeof(UpdateService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";

        // Empty config ⇒ the channel matching this build's OS+arch (e.g. osx-arm64),
        // matching how scripts/pack.sh packed it. Velopack's default channel is
        // per-OS only ("osx") and can't tell Apple Silicon from Intel apart in a
        // shared feed, so we set it explicitly.
        var channel = section["Channel"];
        if (string.IsNullOrWhiteSpace(channel)) channel = DefaultChannel();

        var repoUrl = section.GetSection("Github")["RepoUrl"];
        var prerelease = bool.TryParse(section.GetSection("Github")["Prerelease"], out var pre) && pre;

        if (_policy != UpdatePolicy.Disabled && !string.IsNullOrWhiteSpace(repoUrl))
        {
            try
            {
                // Public repo ⇒ release assets download anonymously, no token needed.
                // Reads releases.<channel>.json + the *.nupkg assets attached to
                // the project's GitHub Releases.
                var source = new GithubSource(repoUrl, accessToken: null, prerelease: prerelease);
                _mgr = new UpdateManager(source, new UpdateOptions { ExplicitChannel = channel });
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Could not initialize the update manager — self-update disabled this session");
            }
        }

        _state = _policy == UpdatePolicy.Disabled ? UpdateState.Disabled
            : _mgr is { IsInstalled: true } ? UpdateState.Idle
            : UpdateState.Unsupported;
    }

    /// <summary>Current snapshot for GET /update/status.</summary>
    public UpdateStatusDto GetStatus()
    {
        lock (_gate)
        {
            return new UpdateStatusDto(
                State: ToWire(_state),
                Policy: ToWire(_policy),
                Supported: _mgr is { IsInstalled: true } && _policy != UpdatePolicy.Disabled,
                Installed: _installedVersion,
                Latest: _latest,
                Percent: _percent,
                Error: _error);
        }
    }

    /// <summary>
    /// Triggers a download-and-apply, started from POST /update. Returns the
    /// status immediately (the web app polls GET /update/status for progress);
    /// the actual work runs in the background and ends by exiting the process.
    /// Idempotent while an update is already in flight.
    /// </summary>
    public UpdateStatusDto RequestUpdate()
    {
        lock (_gate)
        {
            var canUpdate = _mgr is { IsInstalled: true } && _policy != UpdatePolicy.Disabled;
            var alreadyRunning = _state is UpdateState.Checking or UpdateState.Downloading
                or UpdateState.Ready or UpdateState.Applying;
            if (canUpdate && !alreadyRunning)
            {
                _error = null;
                _activeOp = Task.Run(RunUpdateAsync);
            }
        }
        return GetStatus();
    }

    /// <summary>Optional scheduled background check (policy Auto only).</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_policy != UpdatePolicy.Auto || _mgr is not { IsInstalled: true })
            return;

        // Let the agent finish coming up before the first check.
        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var info = await _mgr.CheckForUpdatesAsync();
                if (info != null)
                {
                    _log.LogInformation("Scheduled check found v{Version}; applying", info.TargetFullRelease.Version);
                    RequestUpdate();
                }
                else
                {
                    SetState(UpdateState.UpToDate, latest: _installedVersion);
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Scheduled update check failed");
            }

            try { await Task.Delay(_checkInterval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task RunUpdateAsync()
    {
        try
        {
            SetState(UpdateState.Checking);
            var info = await _mgr!.CheckForUpdatesAsync();
            if (info == null)
            {
                SetState(UpdateState.UpToDate, latest: _installedVersion);
                return;
            }

            var version = info.TargetFullRelease.Version.ToString();
            lock (_gate) { _pending = info; _latest = version; _state = UpdateState.Downloading; _percent = 0; }
            _log.LogInformation("Downloading update v{Version}", version);

            await _mgr.DownloadUpdatesAsync(info, percent =>
            {
                lock (_gate) { _percent = percent; }
            });

            SetState(UpdateState.Ready);
            // Give the web app a beat to observe "ready" before the connection drops.
            await Task.Delay(TimeSpan.FromSeconds(1));

            SetState(UpdateState.Applying);
            _log.LogInformation("Applying update v{Version}; Update.exe will relaunch the new version", version);
            // Process exits here; Update.exe applies the update and relaunches the agent.
            // On macOS, this relaunch bypasses the LaunchAgent entirely, so it must carry
            // --background itself or the freshly-updated process would mistake itself for a
            // manual launch and show the status/uninstall dialog instead of serving the API.
            var restartArgs = OperatingSystem.IsMacOS() ? new[] { "--background" } : null;
            // silent: true — non-silent Update.exe shows GUI dialogs (progress, and an
            // elevation confirm if the bundle isn't writable) that a headless
            // LaunchAgent-spawned process can never bring to the front on macOS 14+;
            // they sit invisible and auto-cancel after 5 minutes, killing the update
            // with the old process already gone. The per-user install never needs
            // elevation, so if permissions are ever wrong we want a fast, logged
            // failure instead of an invisible dialog.
            _mgr.WaitExitThenApplyUpdates(info.TargetFullRelease, silent: true, restart: true, restartArgs);
            // Update.exe only waits 60s for this pid to die, and exit code 0 keeps the
            // LaunchAgent's crash-only KeepAlive from respawning the old agent mid-swap.
            // (ApplyUpdatesAndRestart did this same Exit(0) internally.)
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Update failed");
            Fail(ex);
        }
    }

    private void SetState(UpdateState state, string? latest = null)
    {
        lock (_gate)
        {
            _state = state;
            if (latest != null) _latest = latest;
            if (state != UpdateState.Downloading) _percent = null;
            if (state != UpdateState.Failed) _error = null;
        }
    }

    private void Fail(Exception ex)
    {
        lock (_gate)
        {
            _state = UpdateState.Failed;
            _error = ex.Message;
            _percent = null;
        }
    }

    /// <summary>
    /// The Velopack channel for this build's OS + CPU architecture, matching the
    /// RID scripts/pack.sh packs with (win-x64 / osx-arm64 / osx-x64 / linux-x64).
    /// Uses the *process* architecture so an Intel build under Rosetta correctly
    /// resolves to osx-x64.
    /// </summary>
    private static string DefaultChannel()
    {
        var os = OperatingSystem.IsWindows() ? "win"
            : OperatingSystem.IsMacOS() ? "osx"
            : "linux";
        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X64 => "x64",
            var other => other.ToString().ToLowerInvariant(),
        };
        return $"{os}-{arch}";
    }

    private static string ToWire(UpdateState s) => s switch
    {
        UpdateState.Idle => "idle",
        UpdateState.Checking => "checking",
        UpdateState.UpdateAvailable => "update-available",
        UpdateState.Downloading => "downloading",
        UpdateState.Ready => "ready",
        UpdateState.Applying => "applying",
        UpdateState.UpToDate => "up-to-date",
        UpdateState.Failed => "failed",
        UpdateState.Disabled => "disabled",
        UpdateState.Unsupported => "unsupported",
        _ => "idle",
    };

    private static string ToWire(UpdatePolicy p) => p switch
    {
        UpdatePolicy.Auto => "auto",
        UpdatePolicy.OnDemandOnly => "on-demand",
        UpdatePolicy.Disabled => "disabled",
        _ => "on-demand",
    };
}
