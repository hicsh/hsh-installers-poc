using Microsoft.AspNetCore.SignalR;

namespace HshAgent;

/// <summary>
/// SignalR hub the browser app connects to. Clients only receive here — the
/// server pushes a "RandomNumber" message every couple of seconds from
/// <see cref="RandomFeedService"/>. Connecting at all is what tells the web app
/// the agent is online (mirrors how the production agent's device hub works).
/// </summary>
public sealed class RandomHub : Hub
{
}
