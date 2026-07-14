using System.Reflection;
using HshAgent;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Configuration;
using Velopack;

// Must run before anything else: handles Velopack's install/update/uninstall
// hooks and returns immediately on a normal run.
var velopack = VelopackApp.Build();

if (OperatingSystem.IsWindows())
{
    velopack
        .OnFirstRun(WindowsInstallHooks.FirstRun)
        .OnAfterInstallFastCallback(WindowsInstallHooks.AfterInstall)
        .OnBeforeUninstallFastCallback(WindowsInstallHooks.BeforeUninstall);
}
else if (OperatingSystem.IsMacOS())
{
    velopack.OnFirstRun(MacOsInstallHooks.FirstRun);
}

velopack.Run();

// macOS only: the first-run hook loads a launchd LaunchAgent that immediately
// starts its own managed copy of the agent. Hand off to that single instance
// and exit: if this foreground process also started the web host, the two
// would race to bind the HTTP port and the loser would crash. On Windows the
// hook merely writes the HKCU Run key (nothing starts a second copy), so this
// first-run process falls through and runs the web host itself; the Run key
// covers every subsequent logon.
if (OperatingSystem.IsMacOS() && MacOsInstallHooks.RegisteredSelfStartingAutostart)
{
    Console.WriteLine("First-run setup complete — handing off to the background service.");
    return;
}

// The LaunchAgent always passes --background (see MacOsInstallHooks.RegisterLaunchAgent)
// and so does our own post-update relaunch (see UpdateService.RunUpdateAsync). Its absence
// means a human launched the app directly — e.g. double-clicking it in Finder — so show a
// small status/uninstall dialog instead of racing the LaunchAgent-managed copy for the port.
if (OperatingSystem.IsMacOS() && !args.Contains("--background"))
{
    var probeConfig = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true)
        .Build();
    MacOsInstallHooks.ShowManualLaunchDialog(probeConfig.GetValue("Server:Port", 9740));
    return;
}

var agentVersion = typeof(Program).Assembly
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
    ?.InformationalVersion ?? "0.0.0";

// ContentRootPath must be the executable's own directory, not the process's
// working directory: launchd (macOS LaunchAgent) and Windows scheduled tasks
// don't set a working directory, so the default (cwd) leaves appsettings.json
// unresolved and config silently falls back to defaults (e.g. an empty
// Update:Github:RepoUrl, which disables self-update entirely).
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// localhost-only HTTP API the browser app talks to. Port is configurable so it
// can coexist with anything else on the machine.
var port = builder.Configuration.GetValue("Server:Port", 9740);

builder.Services.AddCors();
builder.Services.AddSignalR();
builder.Services.AddSingleton<UpdateService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<UpdateService>());
builder.Services.AddHostedService<RandomFeedService>();

builder.WebHost.UseUrls($"http://localhost:{port}");
builder.Logging.AddConsole();

var app = builder.Build();

// Permissive CORS: the only caller is the user's own browser on localhost, and
// it needs to reach /version, /update and the hub from any origin the web app
// happens to be served from.
app.UseCors(cors => cors
    .SetIsOriginAllowed(_ => true)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials());

// SignalR hub that streams a random number every couple of seconds. This stands
// in for the real-time device connection in the production agent.
app.MapHub<RandomHub>("/hub");
app.MapGet("/health", () => "HSH Agent running");
app.MapGet("/version", () => Results.Ok(new { version = agentVersion }));
app.MapGet("/random", () => Results.Ok(new { value = Random.Shared.Next() }));

// Self-update, driven by the web app's "Update now" button. GET reports the
// current state; POST kicks off a download-and-apply that ends by exiting the
// process so the service manager relaunches the new version.
app.MapGet("/update/status", (UpdateService updater) => Results.Ok(updater.GetStatus()));
app.MapPost("/update", (UpdateService updater) => Results.Ok(updater.RequestUpdate()));

Console.WriteLine("===========================================================");
Console.WriteLine($"  HSH Agent v{agentVersion} :: http://localhost:{port}/hub");
Console.WriteLine("===========================================================");

try
{
    app.Run();
}
catch (IOException ex) when (ex.InnerException is AddressInUseException)
{
    // Another instance already owns the port. Exit cleanly (code 0) rather than
    // letting the unhandled exception abort the process — an abort is what shows
    // the macOS "quit unexpectedly" crash dialog. The existing instance keeps
    // serving; the service manager leaves it alone.
    Console.Error.WriteLine($"Port {port} already in use — another HSH Agent instance is running. Exiting.");
}
