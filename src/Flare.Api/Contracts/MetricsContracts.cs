using Flare.Infrastructure.Persistence.ReadModels;

namespace Flare.Api.Contracts;

public sealed record ServiceMttrResponse(
    Guid ServiceId,
    string ServiceName,
    long IncidentCount,
    long AvgMttrMs,
    long P50MttrMs);

public sealed record ServiceMttaResponse(
    Guid ServiceId,
    string ServiceName,
    long IncidentCount,
    long AvgMttaMs,
    long P50MttaMs);

public sealed record DashboardResponse(
    long OpenIncidentsCount,
    long OverdueActionItemsCount,
    long MttrLast30dAvgMs);

public static class MetricsMappings
{
    public static ServiceMttrResponse ToResponse(this MttrByServiceRow r) => new(
        r.ServiceId, r.ServiceName, r.IncidentCount, r.AvgMttrMs, r.P50MttrMs);

    public static ServiceMttaResponse ToResponse(this MttaByServiceRow r) => new(
        r.ServiceId, r.ServiceName, r.IncidentCount, r.AvgMttaMs, r.P50MttaMs);
}
