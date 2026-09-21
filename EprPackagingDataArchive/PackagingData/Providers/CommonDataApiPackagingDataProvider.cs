using System.Globalization;
using EprPackagingDataArchive.CommonData;
using EprPackagingDataArchive.CommonData.Client;
using EprPackagingDataArchive.PackagingData.Models;
using EprPackagingDataArchive.Shared;

namespace EprPackagingDataArchive.PackagingData.Providers;

/// <summary>
/// Phase two. Serves the nested report from the Azure Common Data API, which reads Synapse.
///
/// Only <see cref="GetReportAsync"/> is implemented. The other three methods have no upstream
/// equivalent yet and throw rather than return something plausible but wrong, which is the same
/// choice <see cref="DataSourceRegistration"/> makes for the unimplemented modes.
/// </summary>
public sealed class CommonDataApiPackagingDataProvider(
    ICommonDataApiClient client,
    CommonDataApiDataSourceDescriptor descriptor,
    TimeProvider time,
    ILogger<CommonDataApiPackagingDataProvider> logger) : IPackagingDataProvider
{
    public async Task<IReadOnlyCollection<PackagingDataRow>?> GetReportAsync(
        string organisationId,
        ReportQuery query,
        CancellationToken cancellationToken = default)
    {
        // Upstream keys on the EPR reference number as an integer. A non-numeric id cannot identify
        // any organisation, so it is a 404 rather than a 400: the route shape was valid, the thing
        // it named does not exist.
        if (!int.TryParse(organisationId, NumberStyles.None, CultureInfo.InvariantCulture, out var reference))
        {
            logger.LogInformation("Organisation id {OrganisationId} is not an EPR reference number", organisationId);
            return null;
        }

        // The upstream procedure returns accepted submissions only. Asking it for rejected ones and
        // filtering the answer would return an empty report that looks authoritative, so the
        // unanswerable case is refused up front.
        if (query.Status is not null && !query.Status.Equals("accepted", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                "DataSource:Mode=CommonDataApi can only serve accepted submissions. The upstream "
                + "procedure filters on Regulator_Status = 'Accepted', so rejected and pending "
                + "submissions are not reachable through this source.");
        }

        // Refreshed here rather than on a timer, because the traced HTTP client can only be used
        // inside a request. At most one extra upstream call every fifteen minutes.
        await descriptor.EnsureFreshAsync(client, time, logger, cancellationToken);

        var rows = await client.GetOrganisationPomsAsync(reference, RelativeYearFor(query.Year), cancellationToken);

        if (rows.Count == 0)
        {
            // No rows means no accepted packaging data. That is not the same as "no such
            // organisation", but this source cannot tell the two apart: it only ever sees rows that
            // survived every PayCal filter. An empty list keeps that honest, where a 404 would assert
            // something we do not know.
            logger.LogInformation("No packaging rows upstream for organisation {OrganisationId}", reference);
            return [];
        }

        return rows.Select(row => ToRow(organisationId, reference, row)).InReportOrder();
    }

    /// <summary>
    /// Our year is the submission year; upstream's relative year is one greater. The same offset the
    /// obligation year uses, and the reason a caller asking for 2025 gets rows from 2025-H1 and
    /// 2025-H2 rather than nothing.
    /// </summary>
    private static int? RelativeYearFor(int? submissionYear) => submissionYear + 1;

    private static PackagingDataRow ToRow(string organisationId, int reference, UpstreamPomRow row)
    {
        var period = row.SubmissionPeriod.NullIfBlank() ?? "unknown";

        return new PackagingDataRow
        {
            OrganisationId = organisationId,
            SubsidiaryId = row.SubsidiaryId.NullIfBlank(),

            // Upstream returns no submission identifier, so one is derived from the two things that
            // do identify it. Deriving it keeps the value stable across calls, which a random id
            // would not, and a consumer can still use it as an opaque key.
            SubmissionId = $"{reference}-{period}",
            SubmissionPeriod = period,

            // Always accepted, because the procedure filters on it. Stated rather than inferred.
            Status = "accepted",
            PackagingActivity = row.PackagingActivity.NullIfBlank() ?? string.Empty,
            PackagingType = row.PackagingType.NullIfBlank() ?? string.Empty,
            PackagingClass = row.PackagingClass.NullIfBlank(),
            PackagingMaterial = row.PackagingMaterial.NullIfBlank() ?? string.Empty,
            PackagingMaterialSubtype = row.PackagingMaterialSubtype.NullIfBlank(),
            PackagingMaterialWeight = (decimal)(row.PackagingMaterialWeight ?? 0d),

            // Converted at the boundary, per Shared/Nation.cs. Upstream speaks EN/NI/SC/WS.
            FromCountry = NationOrRaw(row.FromCountry),

            // The upstream procedure excludes exports, so this is always null by construction.
            ToCountry = null,
            RamRagRating = row.RamRagRating.NullIfBlank()
        };
    }

    /// <summary>
    /// Falls back to the raw value rather than throwing on an unrecognised nation code. A single
    /// unexpected code should not fail a whole report, and passing it through makes it visible.
    /// </summary>
    private static string? NationOrRaw(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        try
        {
            return Nation.FromWarehouseCode(code.Trim());
        }
        catch (ArgumentOutOfRangeException)
        {
            return code.Trim();
        }
    }

    public Task<IReadOnlyCollection<PackagingDataLine>> GetLinesAsync(
        string organisationId, PackagingDataQuery query, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "GetLines is not implemented for DataSource:Mode=CommonDataApi. Only the nested report "
            + "endpoint has an upstream equivalent today.");

    public Task<PackagingDataSummary> GetSummaryAsync(
        string organisationId, PackagingDataQuery query, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "GetSummary is not implemented for DataSource:Mode=CommonDataApi. Aggregation belongs "
            + "in the source, and the upstream procedure does not do it.");

    public Task<SchemePackagingDataSummary> GetSchemeSummaryAsync(
        string schemeId, PackagingDataQuery query, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "GetSchemeSummary is not implemented for DataSource:Mode=CommonDataApi. There is no "
            + "upstream endpoint that resolves a compliance scheme's membership.");
}
