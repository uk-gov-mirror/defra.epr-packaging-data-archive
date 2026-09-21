using EprPackagingDataArchive.Organisations.Models;
using EprPackagingDataArchive.PackagingData.Models;
using EprPackagingDataArchive.Shared.Stubs;

namespace EprPackagingDataArchive.PackagingData.Providers;

/// <summary>
/// Serves packaging data from <see cref="StubDataSet"/>.
///
/// Summaries are computed from the same line fixtures the detail endpoint returns rather than being
/// separate hardcoded totals. That is not just tidiness: it means a caller who fetches the lines and
/// adds them up gets the summary figure, which is a guarantee a real implementation must also make
/// and which hardcoded fixtures would quietly break.
/// </summary>
public sealed class StubPackagingDataProvider : IPackagingDataProvider
{
    public Task<IReadOnlyCollection<PackagingDataRow>?> GetReportAsync(
        string organisationId,
        ReportQuery query,
        CancellationToken cancellationToken = default)
    {
        var exists = StubDataSet.Organisations.Any(o =>
            string.Equals(o.OrganisationId, organisationId, StringComparison.OrdinalIgnoreCase));

        if (!exists) return Task.FromResult<IReadOnlyCollection<PackagingDataRow>?>(null);

        // Submissions are selected by the organisation the data is ABOUT, so data a scheme filed on a
        // member's behalf appears under the member.
        var submissions = StubDataSet.Submissions
            .Where(s => StubDataSet.SubmissionSubject.TryGetValue(s.SubmissionId, out var subject)
                        && string.Equals(subject, organisationId, StringComparison.OrdinalIgnoreCase))
            .Where(s => query.Year is null
                        || (Shared.SubmissionPeriod.TryParse(s.SubmissionPeriod, out var period)
                            && period.Value.Year == query.Year))
            .Select(s => new { s.SubmissionId, s.SubmissionPeriod, Status = StatusOf(s.Status) })
            .Where(s => query.Status is null
                        || string.Equals(s.Status, query.Status, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(s => s.SubmissionId, StringComparer.OrdinalIgnoreCase);

        IReadOnlyCollection<PackagingDataRow> rows = StubDataSet.PackagingLines
            .Where(l => string.Equals(l.OrganisationId, organisationId, StringComparison.OrdinalIgnoreCase))
            .Where(l => submissions.ContainsKey(l.SubmissionId))
            .Select(l =>
            {
                var submission = submissions[l.SubmissionId];
                return ToRow(l, submission.SubmissionId, submission.SubmissionPeriod, submission.Status);
            })
            .InReportOrder();

        return Task.FromResult<IReadOnlyCollection<PackagingDataRow>?>(rows);
    }

    /// <summary>
    /// The fixtures use the internal status names. The contract speaks one vocabulary, the same one
    /// the Common Data API source produces: accepted, rejected or pending.
    /// </summary>
    private static string StatusOf(string fixtureStatus) =>
        fixtureStatus.StartsWith("Accepted", StringComparison.OrdinalIgnoreCase) ? "accepted"
        : fixtureStatus.StartsWith("Rejected", StringComparison.OrdinalIgnoreCase) ? "rejected"
        : "pending";

    private static PackagingDataRow ToRow(
        PackagingDataLine line, string submissionId, string submissionPeriod, string status) =>
        new()
        {
            OrganisationId = line.OrganisationId,
            SubsidiaryId = line.SubsidiaryId.NullIfBlank(),
            SubmissionId = submissionId,
            SubmissionPeriod = submissionPeriod,
            Status = status,
            PackagingActivity = line.Activity,
            PackagingType = line.PackagingType,
            PackagingClass = line.PackagingClass.NullIfBlank(),
            PackagingMaterial = line.Material,
            PackagingMaterialSubtype = line.MaterialSubtype.NullIfBlank(),
            PackagingMaterialWeight = line.Tonnage,
            PackagingMaterialUnits = line.Units,
            TransitionalPackagingUnits = line.TransitionalPackagingUnits,
            FromCountry = line.FromNation.NullIfBlank(),
            ToCountry = line.ToNation.NullIfBlank(),
            RamRagRating = line.RamRagRating.NullIfBlank()
        };

    public Task<IReadOnlyCollection<PackagingDataLine>> GetLinesAsync(
        string organisationId,
        PackagingDataQuery query,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<PackagingDataLine> results = LinesFor(organisationId, query)
            .OrderBy(l => l.SubmissionPeriod)
            .ThenBy(l => l.Material, StringComparer.Ordinal)
            .ToList();

        return Task.FromResult(results);
    }

    public Task<PackagingDataSummary> GetSummaryAsync(
        string organisationId,
        PackagingDataQuery query,
        CancellationToken cancellationToken = default)
    {
        var lines = LinesFor(organisationId, query).ToList();

        var summary = new PackagingDataSummary
        {
            OrganisationId = organisationId,
            SubmissionPeriod = query.SubmissionPeriod ?? "all",
            ObligationYear = query.ObligationYear ?? lines.Select(l => l.ObligationYear).DefaultIfEmpty(0).Max(),
            Totals = TotalsOf(lines),
            ByMaterial = GroupBy(lines, l => l.Material),
            ByActivity = GroupBy(lines, l => l.Activity),
            ByNation = GroupBy(lines, l => l.FromNation)
        };

        return Task.FromResult(summary);
    }

    public Task<SchemePackagingDataSummary> GetSchemeSummaryAsync(
        string schemeId,
        PackagingDataQuery query,
        CancellationToken cancellationToken = default)
    {
        var memberIds = StubDataSet.SchemeMembers.Select(m => m.OrganisationId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var lines = StubDataSet.PackagingLines
            .Where(l => memberIds.Contains(l.OrganisationId))
            .Where(l => Matches(l, query))
            .ToList();

        var summary = new SchemePackagingDataSummary
        {
            SchemeId = schemeId,
            SubmissionPeriod = query.SubmissionPeriod ?? "all",
            ObligationYear = query.ObligationYear ?? lines.Select(l => l.ObligationYear).DefaultIfEmpty(0).Max(),
            MemberCount = memberIds.Count,
            ReportingMemberCount = lines.Select(l => l.OrganisationId)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            Totals = TotalsOf(lines),
            ByMaterial = GroupBy(lines, l => l.Material),
            ByNation = GroupBy(lines, l => l.FromNation)
        };

        return Task.FromResult(summary);
    }

    private static IEnumerable<PackagingDataLine> LinesFor(string organisationId, PackagingDataQuery query) =>
        StubDataSet.PackagingLines
            .Where(l => string.Equals(l.OrganisationId, organisationId, StringComparison.OrdinalIgnoreCase))
            .Where(l => Matches(l, query));

    private static bool Matches(PackagingDataLine line, PackagingDataQuery query)
    {
        if (query.SubmissionPeriod is not null
            && !string.Equals(line.SubmissionPeriod, query.SubmissionPeriod, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (query.ObligationYear is not null && line.ObligationYear != query.ObligationYear) return false;

        if (query.Material is not null
            && !string.Equals(line.Material, query.Material, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (query.SubmittedBy is null) return true;

        var wantsScheme = string.Equals(query.SubmittedBy, "scheme", StringComparison.OrdinalIgnoreCase);
        var isScheme = line.SubmittedBy.Role == SubmitterRoles.ComplianceScheme;

        return wantsScheme == isScheme;
    }

    private static SummaryTotals TotalsOf(IReadOnlyCollection<PackagingDataLine> lines) =>
        new()
        {
            Tonnage = lines.Sum(l => l.Tonnage),
            LineCount = lines.Count
        };

    private static IReadOnlyCollection<Breakdown> GroupBy(
        IEnumerable<PackagingDataLine> lines,
        Func<PackagingDataLine, string> key) =>
        lines.GroupBy(key)
            .Select(g => new Breakdown
            {
                Key = g.Key,
                Tonnage = g.Sum(l => l.Tonnage),
                LineCount = g.Count()
            })
            .OrderByDescending(b => b.Tonnage)
            .ToList();
}
