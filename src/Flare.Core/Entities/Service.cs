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

    public Service(Guid teamId, string name, string description = "", string runbookBody = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
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
