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
