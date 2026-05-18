namespace Flare.Core.Entities;

public sealed class Service
{
    public Guid Id { get; private set; }
    public Guid TeamId { get; private set; }
    public string Name { get; private set; } = default!;
    public string Description { get; private set; } = string.Empty;
    public string RunbookBody { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; }

    public Team Team { get; private set; } = default!;
    public ICollection<Incident> Incidents { get; private set; } = [];

    private Service() { }

    // Same bound rationale as Incident.MaxTitleLength: admin-set field but still flows
    // through notification channels, so a runaway name length must not amplify into per-tick
    // megabyte allocations across the broadcast fan-out.
    public const int MaxNameLength = 512;

    public Service(Guid teamId, string name, string description = "", string runbookBody = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > MaxNameLength)
            throw new ArgumentException($"Name cannot exceed {MaxNameLength} characters.", nameof(name));
        Id = Guid.NewGuid();
        TeamId = teamId;
        Name = name;
        Description = description;
        RunbookBody = runbookBody;
        CreatedAt = DateTime.UtcNow;
    }

    public void UpdateRunbook(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        RunbookBody = body;
    }
}
