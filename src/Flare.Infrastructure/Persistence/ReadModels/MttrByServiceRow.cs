namespace Flare.Infrastructure.Persistence.ReadModels;

public sealed class MttrByServiceRow
{
    public Guid ServiceId { get; init; }
    public string ServiceName { get; init; } = "";
    public long IncidentCount { get; init; }
    public long AvgMttrMs { get; init; }
    public long P50MttrMs { get; init; }
}
