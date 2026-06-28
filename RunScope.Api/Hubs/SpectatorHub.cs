using Microsoft.AspNetCore.SignalR;

namespace RunScope.Api.Hubs;

public class SpectatorHub : Hub
{
    // Spectators call this to join the SignalR group for an event.
    // The racer's location updates are then broadcast to this group
    // via IHubContext<SpectatorHub> in EventEndpoints.
    public async Task JoinEvent(string eventId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, eventId);
    }

    public async Task LeaveEvent(string eventId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, eventId);
    }
}