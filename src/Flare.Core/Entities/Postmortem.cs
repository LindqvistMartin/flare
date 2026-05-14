namespace Flare.Core.Entities;

public enum PostmortemStatus { Draft, Published }

public sealed class Postmortem
{
    public Guid Id { get; private set; }
    public Guid IncidentId { get; private set; }
    public PostmortemStatus Status { get; private set; }
    public string Impact { get; private set; } = string.Empty;
    public string Timeline { get; private set; } = "[]";
    public string RootCause { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; }
    public DateTime? PublishedAt { get; private set; }

    public Incident Incident { get; private set; } = default!;
    public ICollection<ActionItem> ActionItems { get; private set; } = [];

    private Postmortem() { }

    public Postmortem(Guid incidentId, string impact, string timeline, string rootCause)
    {
        Id = Guid.NewGuid();
        IncidentId = incidentId;
        Status = PostmortemStatus.Draft;
        Impact = impact ?? string.Empty;
        Timeline = string.IsNullOrWhiteSpace(timeline) ? "[]" : timeline;
        RootCause = rootCause ?? string.Empty;
        CreatedAt = DateTime.UtcNow;
    }

    public void Publish()
    {
        Status = PostmortemStatus.Published;
        PublishedAt = DateTime.UtcNow;
    }

    public void Update(string impact, string rootCause)
    {
        if (Status == PostmortemStatus.Published)
            throw new InvalidOperationException("Cannot update a published postmortem.");
        ArgumentNullException.ThrowIfNull(impact);
        ArgumentNullException.ThrowIfNull(rootCause);
        Impact = impact;
        RootCause = rootCause;
    }

    public void Regenerate(string impact, string timeline, string rootCause)
    {
        if (Status == PostmortemStatus.Published)
            throw new InvalidOperationException("Cannot regenerate a published postmortem.");
        ArgumentNullException.ThrowIfNull(impact);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeline);
        ArgumentNullException.ThrowIfNull(rootCause);
        Impact = impact;
        Timeline = timeline;
        RootCause = rootCause;
    }
}
