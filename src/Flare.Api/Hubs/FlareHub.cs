using Microsoft.AspNetCore.SignalR;

namespace Flare.Api.Hubs;

public sealed class FlareHub : Hub
{
    public Task JoinDashboard() =>
        Groups.AddToGroupAsync(Context.ConnectionId, "dashboard");

    public Task LeaveDashboard() =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, "dashboard");

    public Task JoinIncident(Guid incidentId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, $"incident:{incidentId}");

    public Task LeaveIncident(Guid incidentId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, $"incident:{incidentId}");
}
