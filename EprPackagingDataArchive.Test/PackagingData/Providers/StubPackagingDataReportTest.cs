using EprPackagingDataArchive.PackagingData.Providers;

namespace EprPackagingDataArchive.Test.PackagingData.Providers;

/// <summary>
/// Unit tests for the flat Get Packaging Data rows: one row per packaging line, each carrying the
/// submission it came from.
/// </summary>
public class StubPackagingDataReportTest
{
    private readonly IPackagingDataProvider _provider = new StubPackagingDataProvider();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Returns_null_for_an_unknown_organisation()
    {
        Assert.Null(await _provider.GetReportAsync("000000", new ReportQuery(), Token));
    }

    [Fact]
    public async Task An_organisation_with_no_data_gets_an_empty_list_not_null()
    {
        var rows = await _provider.GetReportAsync("100999", new ReportQuery(), Token);

        Assert.NotNull(rows);
        Assert.Empty(rows);
    }

    [Fact]
    public async Task Every_row_carries_its_organisation_and_submission()
    {
        var rows = await _provider.GetReportAsync("100123", new ReportQuery(), Token);

        Assert.NotNull(rows);
        Assert.Equal(9, rows.Count);
        Assert.All(rows, r => Assert.Equal("100123", r.OrganisationId));
        Assert.All(rows, r => Assert.False(string.IsNullOrEmpty(r.SubmissionId)));

        // Three submissions, still distinguishable from the rows alone.
        Assert.Equal(3, rows.Select(r => r.SubmissionId).Distinct().Count());
    }

    [Fact]
    public async Task Rows_come_back_in_period_then_type_then_material_order()
    {
        var rows = await _provider.GetReportAsync("100123", new ReportQuery(), Token);

        Assert.NotNull(rows);
        var keys = rows.Select(r => (r.SubmissionPeriod, r.PackagingType, r.PackagingMaterial)).ToList();
        var sorted = keys
            .OrderBy(k => k.SubmissionPeriod, StringComparer.Ordinal)
            .ThenBy(k => k.PackagingType, StringComparer.Ordinal)
            .ThenBy(k => k.PackagingMaterial, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(sorted, keys);
    }

    [Fact]
    public async Task Year_filters_on_the_submission_period_year()
    {
        var rows = await _provider.GetReportAsync("100123", new ReportQuery { Year = 2025 }, Token);

        Assert.NotNull(rows);
        // 2025-H2 accepted and 2025-H1 rejected; the 2026-H1 submission is excluded.
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.StartsWith("2025-", r.SubmissionPeriod));
        Assert.Equal(2, rows.Select(r => r.SubmissionId).Distinct().Count());
    }

    [Fact]
    public async Task Status_rejected_returns_only_rows_from_rejected_submissions()
    {
        var rows = await _provider.GetReportAsync("100123", new ReportQuery { Status = "rejected" }, Token);

        Assert.NotNull(rows);
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Equal("rejected", r.Status));
        Assert.All(rows, r => Assert.Equal("2025-H1", r.SubmissionPeriod));
    }

    [Fact]
    public async Task Status_uses_the_same_vocabulary_as_the_real_source()
    {
        var rows = await _provider.GetReportAsync("100123", new ReportQuery(), Token);

        Assert.NotNull(rows);
        // The fixtures say AcceptedByRegulator and so on; the contract says accepted.
        Assert.All(rows, r => Assert.Contains(r.Status, new[] { "accepted", "rejected", "pending" }));
    }

    [Fact]
    public async Task Rows_carry_the_ticket_fields()
    {
        var rows = await _provider.GetReportAsync(
            "100123", new ReportQuery { Year = 2025, Status = "rejected" }, Token);

        Assert.NotNull(rows);

        var subsidiaryRow = Assert.Single(rows, r => r.SubsidiaryId is not null);
        Assert.Equal("100123-S01", subsidiaryRow.SubsidiaryId);
        Assert.Equal(1200, subsidiaryRow.TransitionalPackagingUnits);

        var parentRow = Assert.Single(rows, r => r.SubsidiaryId is null);
        Assert.Equal("PVC", parentRow.PackagingMaterialSubtype);
        Assert.Equal(120.00m, parentRow.PackagingMaterialWeight);
        Assert.Equal("GB-ENG", parentRow.FromCountry);
    }
}
