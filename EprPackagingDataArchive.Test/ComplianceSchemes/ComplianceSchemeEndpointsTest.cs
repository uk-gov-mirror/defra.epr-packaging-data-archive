using System.Net;
using EprPackagingDataArchive.ComplianceSchemes.Models;
using EprPackagingDataArchive.PackagingData.Models;

namespace EprPackagingDataArchive.Test.ComplianceSchemes;

public class ComplianceSchemeEndpointsTest
{
    private const string SchemeId = "CS-004";

    [Fact]
    public async Task Get_members_returns_the_scheme_membership()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new ApiTestFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/compliance-schemes/{SchemeId}/members", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var envelope = await response.ReadEnvelopeAsync<IReadOnlyCollection<SchemeMember>>(cancellationToken);

        Assert.Equal(3, envelope.Data.Count);
        Assert.Contains(envelope.Data, m => m.Status == MembershipStatuses.Left);
    }

    [Fact]
    public async Task Get_members_as_at_a_date_excludes_producers_who_had_left()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new ApiTestFactory();
        using var client = factory.CreateClient();

        // Departed Packaging left on 2026-03-31, so it was not a member in June.
        var response = await client.GetAsync(
            $"/compliance-schemes/{SchemeId}/members?asAt=2026-06-30", cancellationToken);
        var envelope = await response.ReadEnvelopeAsync<IReadOnlyCollection<SchemeMember>>(cancellationToken);

        Assert.DoesNotContain(envelope.Data, m => m.OrganisationId == "100777");
        Assert.Equal(2, envelope.Data.Count);
    }

    [Fact]
    public async Task Get_members_returns_not_found_for_an_unknown_scheme()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new ApiTestFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/compliance-schemes/CS-999/members", cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Reporting_status_separates_members_who_reported_from_those_who_did_not()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new ApiTestFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/compliance-schemes/{SchemeId}/reporting-status?submissionPeriod=2026-H1", cancellationToken);
        var envelope = await response.ReadEnvelopeAsync<SchemeReportingStatus>(cancellationToken);

        Assert.Equal(3, envelope.Data.Summary.Members);
        Assert.Equal(1, envelope.Data.Summary.Reported);
        Assert.Equal(2, envelope.Data.Summary.NotReported);
        Assert.Equal(2027, envelope.Data.ObligationYear);

        var reported = envelope.Data.Members.Single(m => m.Reported);
        Assert.Equal("100456", reported.OrganisationId);
        Assert.Equal(133.80m, reported.Tonnage);

        // Null rather than zero, so "reported nil" and "did not report" stay distinguishable.
        Assert.All(envelope.Data.Members.Where(m => !m.Reported), m => Assert.Null(m.Tonnage));
    }

    [Fact]
    public async Task Reporting_status_requires_a_period()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new ApiTestFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/compliance-schemes/{SchemeId}/reporting-status", cancellationToken);

        // "Who has not reported" is meaningless without a period, so this is a 400 rather than a default.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Scheme_summary_rolls_up_across_members_only()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new ApiTestFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/compliance-schemes/{SchemeId}/packaging-data/summary?submissionPeriod=2026-H1",
            cancellationToken);
        var envelope = await response.ReadEnvelopeAsync<SchemePackagingDataSummary>(cancellationToken);

        // 100123 is a direct producer with far more tonnage; it must not leak into a scheme rollup.
        Assert.Equal(133.80m, envelope.Data.Totals.Tonnage);
        Assert.Equal(3, envelope.Data.MemberCount);
        Assert.Equal(1, envelope.Data.ReportingMemberCount);
    }
}
