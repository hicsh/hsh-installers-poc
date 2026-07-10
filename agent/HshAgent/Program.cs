using System.Reflection;
using HshAgent;
using Microsoft.AspNetCore.Connections;
using Velopack;
using Velopack.Sources;

// Must run before anything else: handles Velopack's install/update/uninstall
// hooks and returns immediately on a normal run. OnFirstRun is cross-platform
// (registers the macOS LaunchAgent / Linux systemd user unit); the *FastCallback
// hooks are Windows-only (elevated by Setup.exe — service registration).
var velopack = VelopackApp.Build()
    .OnFirstRun(InstallHooks.FirstRun);
if (OperatingSystem.IsWindows())
{
    velopack
        .OnAfterInstallFastCallback(InstallHooks.AfterInstall)
        .OnBeforeUninstallFastCallback(InstallHooks.BeforeUninstall);
}
velopack.Run();

// On the first launch after install we just registered an OS autostart entry
// (launchd LaunchAgent / systemd user unit) that immediately starts its own
// managed copy of the agent. Hand off to that single instance and exit: if this
// foreground process also started the web host, the two would race to bind the
// HTTP port and the loser would crash — and under KeepAlive that becomes a
// relaunch/crash loop. Later launches (by the service manager) skip OnFirstRun.
if (InstallHooks.RegisteredSelfStartingAutostart)
{
    Console.WriteLine("First-run setup complete — handing off to the background service.");
    return;
}

var agentVersion = typeof(Program).Assembly
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
    ?.InformationalVersion ?? "0.0.0";

var builder = WebApplication.CreateBuilder(args);

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

Console.WriteLine("=================================");
Console.WriteLine($"  HSH Agent v{agentVersion} :: http://localhost:{port}/hub");
Console.WriteLine("=================================");

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
