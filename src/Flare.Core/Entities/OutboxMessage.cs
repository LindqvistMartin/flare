namespace Flare.Core.Entities;

public sealed class OutboxMessage
{
    public Guid Id { get; private set; }
    public string Type { get; private set; } = default!;
    public string Payload { get; private set; } = default!;
    public DateTime CreatedAt { get; private set; }
    public DateTime? ProcessedAt { get; private set; }

    private OutboxMessage() { }

    public OutboxMessage(string type, string payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);
        Id = Guid.NewGuid();
        Type = type;
        Payload = payload;
        CreatedAt = DateTime.UtcNow;
    }

    public void MarkProcessed() => ProcessedAt = DateTime.UtcNow;
}
