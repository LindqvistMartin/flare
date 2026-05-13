namespace Flare.Core.Entities;

public enum ActionItemStatus { Open, InProgress, Done }

public sealed class ActionItem
{
    public Guid Id { get; private set; }
    public Guid PostmortemId { get; private set; }
    public string Title { get; private set; } = default!;
    public Guid? OwnerId { get; private set; }
    public DateOnly? DueDate { get; private set; }
    public ActionItemStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public Postmortem Postmortem { get; private set; } = default!;

    private ActionItem() { }

    public ActionItem(Guid postmortemId, string title, Guid? ownerId = null, DateOnly? dueDate = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        Id = Guid.NewGuid();
        PostmortemId = postmortemId;
        Title = title;
        OwnerId = ownerId;
        DueDate = dueDate;
        Status = ActionItemStatus.Open;
        CreatedAt = DateTime.UtcNow;
    }

    public void Transition(ActionItemStatus next) => Status = next;

    public void UpdateOwner(Guid? ownerId) => OwnerId = ownerId;

    public void UpdateDueDate(DateOnly? dueDate) => DueDate = dueDate;
}
