using EprPackagingDataArchive.PackagingData.Models;

namespace EprPackagingDataArchive.PackagingData.Providers;

/// <summary>
/// The port for reported packaging data. See <see cref="Organisations.Providers.IOrganisationProvider"/>
/// for why these interfaces are shaped around the domain rather than around any particular source.
/// </summary>
public interface IPackagingDataProvider
{
    /// <summary>
    /// The organisation's packaging data as flat rows, in <see cref="PackagingDataRowOrdering.InReportOrder"/>
    /// order. Null when the organisation is unknown, so the endpoint can 404; an organisation with
    /// nothing reported returns an empty collection instead, keeping "exists but silent"
    /// distinguishable from "does not exist".
    /// </summary>
    Task<IReadOnlyCollection<PackagingDataRow>?> GetReportAsync(
        string organisationId,
        ReportQuery query,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<PackagingDataLine>> GetLinesAsync(
        string organisationId,
        PackagingDataQuery query,
        CancellationToken cancellationToken = default);

    Task<PackagingDataSummary> GetSummaryAsync(
        string organisationId,
        PackagingDataQuery query,
        CancellationToken cancellationToken = default);

    Task<SchemePackagingDataSummary> GetSchemeSummaryAsync(
        string schemeId,
        PackagingDataQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>Filters for the packaging data rows, per the ticket: year and submission status.</summary>
public sealed record ReportQuery
{
    /// <summary>Submission year: 2025 matches 2025-H1, 2025-H2 and 2025-P0. See DECISIONS.md 2.</summary>
    public int? Year { get; init; }

    /// <summary>accepted or rejected, matched against the fuller internal status. See DECISIONS.md 3.</summary>
    public string? Status { get; init; }
}

public sealed record PackagingDataQuery
{
    public string? SubmissionPeriod { get; init; }

    public int? ObligationYear { get; init; }

    public string? Material { get; init; }

    /// <summary>Self or Scheme. Filters on who filed the data, not whose data it is.</summary>
    public string? SubmittedBy { get; init; }
}
