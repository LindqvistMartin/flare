namespace Flare.Infrastructure.Persistence.ReadModels;

public sealed class MttaByServiceRow
{
    public Guid ServiceId { get; init; }
    public string ServiceName { get; init; } = "";
    public long IncidentCount { get; init; }
    public long AvgMttaMs { get; init; }
    public long P50MttaMs { get; init; }
}
