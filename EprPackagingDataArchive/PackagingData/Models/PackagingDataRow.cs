namespace EprPackagingDataArchive.PackagingData.Models;

/// <summary>
/// One line of reported packaging data, flat, with the submission it came from on the row.
///
/// Replaces the earlier nested organisation, submissions, rows shape. Consumers filter by
/// organisation and year and want the rows; grouping by submission added a level they then had to
/// flatten themselves. The submission is still identifiable from <see cref="SubmissionId"/>,
/// <see cref="SubmissionPeriod"/> and <see cref="Status"/>.
///
/// There is deliberately no per-row id. Upstream POM rows carry no key, and an id derived from
/// type, material and class collided on real data because rows also differ by activity and
/// subtype. When the archive holds its own store, rows get a real key.
///
/// Field names follow the packaging data CSV headers so the mapping from any source stays
/// mechanical. A field a source does not provide is null, never an empty string.
/// </summary>
public sealed record PackagingDataRow
{
    /// <summary>The EPR reference number of the organisation the data is about.</summary>
    public required string OrganisationId { get; init; }

    public string? SubsidiaryId { get; init; }

    /// <summary>Stable and unique per submission. Derived, since upstream has no submission id.</summary>
    public required string SubmissionId { get; init; }

    /// <summary>For example 2026-H1, or 2024-P1 for the historic four-period year.</summary>
    public required string SubmissionPeriod { get; init; }

    /// <summary>accepted, rejected or pending.</summary>
    public required string Status { get; init; }

    public required string PackagingActivity { get; init; }

    public required string PackagingType { get; init; }

    /// <summary>Null where the type has no class, such as HDC glass.</summary>
    public string? PackagingClass { get; init; }

    public required string PackagingMaterial { get; init; }

    public string? PackagingMaterialSubtype { get; init; }

    /// <summary>Unit deliberately unstated, mirroring the upstream column. See DECISIONS.md 5.</summary>
    public required decimal PackagingMaterialWeight { get; init; }

    public int? PackagingMaterialUnits { get; init; }

    public int? TransitionalPackagingUnits { get; init; }

    public string? FromCountry { get; init; }

    public string? ToCountry { get; init; }

    public string? RamRagRating { get; init; }
}

/// <summary>
/// One ordering for every source, so stub and real responses read the same and a consumer diffing
/// two calls sees no churn: by period, then packaging type, then material.
/// </summary>
public static class PackagingDataRowOrdering
{
    public static IReadOnlyCollection<PackagingDataRow> InReportOrder(this IEnumerable<PackagingDataRow> rows) =>
        rows.OrderBy(r => r.SubmissionPeriod, StringComparer.Ordinal)
            .ThenBy(r => r.PackagingType, StringComparer.Ordinal)
            .ThenBy(r => r.PackagingMaterial, StringComparer.Ordinal)
            .ThenBy(r => r.PackagingActivity, StringComparer.Ordinal)
            .ToList();

    /// <summary>Upstream sends empty strings for absent values; the contract says null.</summary>
    public static string? NullIfBlank(this string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
