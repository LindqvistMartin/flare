namespace Flare.Core.Workers;

public sealed record IngestionJob(
    string Source,
    string RawBody,
    IReadOnlyDictionary<string, string> Headers);
