using Flare.Core.Entities;

namespace Flare.Api.Contracts;

public sealed record CreateActionItemRequest(string Title, Guid? OwnerId, DateOnly? DueDate);

public sealed record PatchActionItemRequest(string? Status, Guid? OwnerId, DateOnly? DueDate);

public sealed record ActionItemResponse(
    Guid Id,
    Guid PostmortemId,
    string Title,
    Guid? OwnerId,
    DateOnly? DueDate,
    string Status,
    DateTime CreatedAt);

public static class ActionItemMappings
{
    public static ActionItemResponse ToResponse(this ActionItem a) => new(
        a.Id, a.PostmortemId, a.Title, a.OwnerId, a.DueDate, a.Status.ToString(), a.CreatedAt);
}
