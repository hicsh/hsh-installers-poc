using Microsoft.AspNetCore.SignalR;

namespace HshAgent;

/// <summary>
/// Pushes a fresh random number to every connected client every couple of
/// seconds. This simulates the real-time stream the production agent would feed
/// from the USB device — here it's just noise so the web app has something live
/// to display while the connection is up.
/// </summary>
public sealed class RandomFeedService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    private readonly IHubContext<RandomHub> _hub;
    private readonly ILogger<RandomFeedService> _log;

    public RandomFeedService(IHubContext<RandomHub> hub, ILogger<RandomFeedService> log)
    {
        _hub = hub;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("Random feed started — emitting every {Seconds}s", Interval.TotalSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            var value = Random.Shared.Next(0, 1_000_000);
            try
            {
                await _hub.Clients.All.SendAsync("RandomNumber", value, stoppingToken);
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Failed to broadcast random number");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
