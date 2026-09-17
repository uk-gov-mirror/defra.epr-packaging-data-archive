using System.Text.Json.Nodes;
using EprPackagingDataArchive.CommonData;
using EprPackagingDataArchive.CommonData.Client;
using EprPackagingDataArchive.PackagingData.Providers;
using EprPackagingDataArchive.Shared;
using Microsoft.Extensions.Logging.Abstractions;

namespace EprPackagingDataArchive.Test.CommonData;

/// <summary>
/// The mapping from the upstream row shape onto the contract.
///
/// A hand written fake client stands in for the Common Data API here, which is a departure from the
/// rest of this suite. Elsewhere the stub providers are already the test double so nothing needs
/// faking; this class exists precisely to translate a foreign shape, and the only way to test that
/// is to hand it one.
/// </summary>
public class CommonDataApiPackagingDataProviderTest
{
    private static CommonDataApiPackagingDataProvider ProviderFor(params UpstreamPomRow[] rows) =>
        Build(new FakeCommonDataApiClient(rows));

    private static CommonDataApiPackagingDataProvider Build(ICommonDataApiClient client) =>
        new(client,
            new CommonDataApiDataSourceDescriptor(),
            TimeProvider.System,
            NullLogger<CommonDataApiPackagingDataProvider>.Instance);

    private static UpstreamPomRow Row(
        string period = "2024-H1",
        string material = "PL",
        string fromCountry = "EN",
        double weight = 100d) =>
        new()
        {
            OrganisationId = 100005,
            OrganisationName = "Test Producer Ltd",
            SubmissionPeriod = period,
            PackagingActivity = "SO",
            PackagingType = "HH",
            PackagingClass = "P1",
            PackagingMaterial = material,
            PackagingMaterialWeight = weight,
            FromCountry = fromCountry
        };

    [Fact]
    public async Task Rows_are_grouped_into_one_submission_per_period()
    {
        var provider = ProviderFor(
            Row(period: "2024-H1", material: "PL"),
            Row(period: "2024-H1", material: "GL"),
            Row(period: "2024-H2", material: "PL"));

        var report = await provider.GetReportAsync("100005", new ReportQuery(), TestContext.Current.CancellationToken);

        Assert.NotNull(report);
        Assert.Equal(2, report.Submissions.Count);
        Assert.Equal(["2024-H1", "2024-H2"], report.Submissions.Select(s => s.SubmissionPeriod));
        Assert.Equal(2, report.Submissions.First().PackagingData.Count);
    }

    [Fact]
    public async Task Organisation_name_comes_from_the_rows()
    {
        var provider = ProviderFor(Row());

        var report = await provider.GetReportAsync("100005", new ReportQuery(), TestContext.Current.CancellationToken);

        Assert.Equal("Test Producer Ltd", report!.Organisation.Name);
        Assert.Equal("100005", report.Organisation.OrganisationId);
    }

    [Fact]
    public async Task Nation_codes_are_converted_at_the_boundary()
    {
        var provider = ProviderFor(Row(fromCountry: "EN"), Row(material: "GL", fromCountry: "WS"));

        var report = await provider.GetReportAsync("100005", new ReportQuery(), TestContext.Current.CancellationToken);

        var nations = report!.Submissions.Single().PackagingData.Select(r => r.FromCountry).ToList();

        Assert.Contains(Nation.England, nations);
        Assert.Contains(Nation.Wales, nations);
    }

    [Fact]
    public async Task An_unrecognised_nation_code_is_passed_through_rather_than_failing_the_report()
    {
        var provider = ProviderFor(Row(fromCountry: "ZZ"));

        var report = await provider.GetReportAsync("100005", new ReportQuery(), TestContext.Current.CancellationToken);

        Assert.Equal("ZZ", report!.Submissions.Single().PackagingData.Single().FromCountry);
    }

    [Fact]
    public async Task Every_submission_is_reported_as_accepted_because_the_source_returns_nothing_else()
    {
        var provider = ProviderFor(Row());

        var report = await provider.GetReportAsync("100005", new ReportQuery(), TestContext.Current.CancellationToken);

        Assert.All(report!.Submissions, submission => Assert.Equal("accepted", submission.Status));
    }

    [Fact]
    public async Task A_year_is_translated_into_the_upstream_relative_year()
    {
        var client = new FakeCommonDataApiClient([Row()]);
        var provider = Build(client);

        await provider.GetReportAsync("100005", new ReportQuery { Year = 2024 }, TestContext.Current.CancellationToken);

        Assert.Equal(2025, client.LastRelativeYear);
    }

    [Fact]
    public async Task No_year_asks_upstream_for_every_year()
    {
        var client = new FakeCommonDataApiClient([Row()]);
        var provider = Build(client);

        await provider.GetReportAsync("100005", new ReportQuery(), TestContext.Current.CancellationToken);

        Assert.Null(client.LastRelativeYear);
    }

    [Fact]
    public async Task An_organisation_with_no_rows_is_reported_with_an_empty_submission_list()
    {
        var provider = ProviderFor();

        var report = await provider.GetReportAsync("100005", new ReportQuery(), TestContext.Current.CancellationToken);

        Assert.NotNull(report);
        Assert.Empty(report.Submissions);
    }

    [Fact]
    public async Task A_non_numeric_organisation_id_cannot_name_anything_upstream()
    {
        var provider = ProviderFor(Row());

        var report = await provider.GetReportAsync("not-a-reference", new ReportQuery(), TestContext.Current.CancellationToken);

        Assert.Null(report);
    }

    [Fact]
    public async Task Asking_for_rejected_submissions_fails_rather_than_returning_an_empty_report()
    {
        var provider = ProviderFor(Row());

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            provider.GetReportAsync("100005", new ReportQuery { Status = "rejected" }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Weight_survives_the_double_to_decimal_conversion()
    {
        var provider = ProviderFor(Row(weight: 334343.5d));

        var report = await provider.GetReportAsync("100005", new ReportQuery(), TestContext.Current.CancellationToken);

        Assert.Equal(334343.5m, report!.Submissions.Single().PackagingData.Single().PackagingMaterialWeight);
    }

    private sealed class FakeCommonDataApiClient(IReadOnlyCollection<UpstreamPomRow> rows) : ICommonDataApiClient
    {
        public int? LastRelativeYear { get; private set; }

        public Task<IReadOnlyCollection<UpstreamPomRow>> GetOrganisationPomsAsync(
            int organisationId, int? relativeYear, CancellationToken cancellationToken)
        {
            LastRelativeYear = relativeYear;
            return Task.FromResult(rows);
        }

        public Task<UpstreamResult> GetLastSyncTimeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new UpstreamResult
            {
                Upstream = new UpstreamCall { Method = "GET", Url = "test", Status = 200, ElapsedMs = 0 },
                Payload = JsonNode.Parse("""{"lastSyncTime":"2026-09-04T15:20:30"}""")
            });

        public Task<UpstreamResult> GetPomSummaryAsync(string organisationReference, int pageSize, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<UpstreamResult> GetPomSampleAsync(int relativeYear, int take, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
