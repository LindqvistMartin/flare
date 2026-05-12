namespace Flare.Core.Entities;

public sealed class Organization
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = default!;
    public string Slug { get; private set; } = default!;
    public DateTime CreatedAt { get; private set; }

    public ICollection<Team> Teams { get; private set; } = [];

    private Organization() { }

    public Organization(string name, string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        Id = Guid.NewGuid();
        Name = name;
        Slug = slug;
        CreatedAt = DateTime.UtcNow;
    }
}
