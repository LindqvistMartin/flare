using System.Text.Json;
using System.Text.RegularExpressions;

namespace Flare.Core.Entities;

public sealed class StatusPage
{
    public const int MaxSlugLength = 64;
    public const int MaxTitleLength = 200;
    public const int MaxDescriptionLength = 1000;
    public const int MaxServices = 50;

    // Lowercase alnum + hyphen, no leading or trailing hyphen. URL-segment shape only —
    // anything else breaks public-page links and forces an encoder dance in the frontend later.
    private static readonly Regex SlugPattern = new(
        "^[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?$",
        RegexOptions.Compiled);

    public Guid Id { get; private set; }
    public Guid OrgId { get; private set; }
    public string Slug { get; private set; } = default!;
    public string Title { get; private set; } = default!;
    public string? Description { get; private set; }
    public string ServiceIdsJson { get; private set; } = "[]";
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public Organization Organization { get; private set; } = default!;

    private StatusPage() { }

    public StatusPage(Guid orgId, string slug, string title, string? description, IReadOnlyList<Guid> serviceIds)
    {
        if (orgId == Guid.Empty)
            throw new ArgumentException("OrgId cannot be empty.", nameof(orgId));
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(serviceIds);

        var normalizedSlug = NormalizeSlug(slug);
        ValidateSlug(normalizedSlug);
        ValidateTitle(title);
        ValidateDescription(description);
        var normalizedServices = NormalizeServiceIds(serviceIds);

        Id = Guid.NewGuid();
        OrgId = orgId;
        Slug = normalizedSlug;
        Title = title;
        Description = string.IsNullOrWhiteSpace(description) ? null : description;
        ServiceIdsJson = JsonSerializer.Serialize(normalizedServices);
        var now = DateTime.UtcNow;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public IReadOnlyList<Guid> GetServiceIds()
        => JsonSerializer.Deserialize<List<Guid>>(ServiceIdsJson) ?? [];

    public void UpdateTitle(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ValidateTitle(title);
        Title = title;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateDescription(string? description)
    {
        ValidateDescription(description);
        Description = string.IsNullOrWhiteSpace(description) ? null : description;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateServices(IReadOnlyList<Guid> serviceIds)
    {
        ArgumentNullException.ThrowIfNull(serviceIds);
        var normalized = NormalizeServiceIds(serviceIds);
        ServiceIdsJson = JsonSerializer.Serialize(normalized);
        UpdatedAt = DateTime.UtcNow;
    }

    private static string NormalizeSlug(string slug) => slug.Trim().ToLowerInvariant();

    private static void ValidateSlug(string slug)
    {
        if (slug.Length is < 1 or > MaxSlugLength)
            throw new ArgumentException($"Slug must be 1-{MaxSlugLength} characters.", nameof(slug));
        if (!SlugPattern.IsMatch(slug))
            throw new ArgumentException(
                "Slug must be lowercase alnum or hyphen, no leading or trailing hyphen.",
                nameof(slug));
    }

    private static void ValidateTitle(string title)
    {
        if (title.Length > MaxTitleLength)
            throw new ArgumentException($"Title cannot exceed {MaxTitleLength} characters.", nameof(title));
    }

    private static void ValidateDescription(string? description)
    {
        if (description is { Length: > MaxDescriptionLength })
            throw new ArgumentException(
                $"Description cannot exceed {MaxDescriptionLength} characters.",
                nameof(description));
    }

    private static List<Guid> NormalizeServiceIds(IReadOnlyList<Guid> serviceIds)
    {
        if (serviceIds.Count == 0)
            throw new ArgumentException("At least one service is required.", nameof(serviceIds));
        if (serviceIds.Count > MaxServices)
            throw new ArgumentException(
                $"Cannot exceed {MaxServices} services per page.",
                nameof(serviceIds));
        if (serviceIds.Any(id => id == Guid.Empty))
            throw new ArgumentException("ServiceIds cannot contain empty GUIDs.", nameof(serviceIds));

        var deduped = serviceIds.Distinct().ToList();
        if (deduped.Count != serviceIds.Count)
            throw new ArgumentException("ServiceIds must be unique.", nameof(serviceIds));
        return deduped;
    }
}
