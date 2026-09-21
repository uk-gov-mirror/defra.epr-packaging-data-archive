using System.Net;
using System.Net.Http.Json;
using EprPackagingDataArchive.Organisations.Models;
using EprPackagingDataArchive.PackagingData.Models;
using EprPackagingDataArchive.Shared;
using Microsoft.AspNetCore.Mvc;

namespace EprPackagingDataArchive.Test.Organisations;

public class OrganisationEndpointsTest
{
    private const string DirectProducer = "100123";
    private const string SchemeMember = "100456";
    private const string NoDataProducer = "100999";

    [Fact]
    public async Task Health_endpoint_is_available()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new ApiTestFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_organisation_returns_a_direct_producer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new ApiTestFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/organisations/{DirectProducer}", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var envelope = await response.ReadEnvelopeAsync<OrganisationResponse>(cancellationToken);

        Assert.Equal(DirectProducer, envelope.Data.OrganisationId);
        Assert.Equal(OrganisationTypes.DirectProducer, envelope.Data.Type);
        Assert.Null(envelope.Data.ComplianceScheme);
        Assert.Equal(DataSourceNames.Stub, envelope.Meta.Source);
    }

    [Fact]
    public async Task Get_organisation_returns_membership_dates_for_a_scheme_member()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new ApiTestFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/organisations/{SchemeMember}", cancellationToken);
        var envelope = await response.ReadEnvelopeAsync<OrganisationResponse>(cancellationToken);

        Assert.Equal(OrganisationTypes.SchemeMember, envelope.Data.Type);
        Assert.NotNull(envelope.Data.ComplianceScheme);
        Assert.Equal("CS-004", envelope.Data.ComplianceScheme.SchemeId);
        // Membership is dated on both ends so that mid-year change is representable.
        Assert.Equal(new DateOnly(2026, 1, 1), envelope.Data.ComplianceScheme.JoinedOn);
    }

    [Fact]
    public async Task Get_organisation_returns_not_found_for_an_unknown_id()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new ApiTestFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/organisations/000000", cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_submissions_returns_newest_first_with_paging_meta()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new ApiTestFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/organisations/{DirectProducer}/submissions", cancellationToken);
        var envelope = await response.ReadEnvelopeAsync<IReadOnlyCollection<SubmissionResponse>>(cancellationToken);

        Assert.Equal(3, envelope.Data.Count);
        Assert.Equal("2026-H1", envelope.Data.First().SubmissionPeriod);
        Assert.NotNull(envelope.Meta.Page);
        Assert.Equal(3, envelope.Meta.Page.Total);
    }

    [Fact]
    public async Task Get_submissions_filters_by_period()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new ApiTestFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/organisations/{DirectProducer}/submissions?submissionPeriod=2025-H2", cancellationToken);
        var envelope = await response.ReadEnvelopeAsync<IReadOnlyCollection<SubmissionResponse>>(cancellationToken);

        Assert.Single(envelope.Data);
        Assert.Equal(2026, envelope.Data.First().ObligationYear);
    }

    [Fact]
    public async Task Get_submissions_returns_empty_array_not_no_content()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new ApiTestFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/organisations/{NoDataProducer}/submissions", cancellationToken);

        // Upstream returns 204 for an empty collection and every consumer has to special-case it.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await response.ReadEnvelopeAsync<IReadOnlyCollection<SubmissionResponse>>(cancellationToken);
        Assert.Empty(envelope.Data);
    }

    [Fact]
    public async Task Get_submission_detail_exposes_warehouse_availability()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new ApiTestFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/organisations/{SchemeMember}/submissions/4e0a91c3-77bf-4d18-b6a2-5f13c8e9d024",
            cancellationToken);
        var envelope = await response.ReadEnvelopeAsync<SubmissionDetailResponse>(cancellationToken);

        Assert.True(envelope.Data.IsResubmission);
        // Submitted, but the ETL has not landed it in the warehouse yet. The two states differ.
        Assert.False(envelope.Data.AvailableInWarehouse);
        Assert.Equal(SubmitterRoles.ComplianceScheme, envelope.Data.SubmittedBy.Role);
    }

    [Fact]
    public async Task Get_submission_does_not_leak_across_organisations()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new ApiTestFactory();
        using var client = factory.CreateClient();

        // A real submission id, but belonging to a different organisation.
        var response = await client.GetAsync(
            $"/organisations/{DirectProducer}/submissions/4e0a91c3-77bf-4d18-b6a2-5f13c8e9d024",
            cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Packaging_data_returns_flat_rows()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new ApiTestFactory();
        using var client = factory.CreateClient();

        // Data filed BY the scheme still appears under the member the data is about.
        var response = await client.GetAsync(
            $"/organisations/{SchemeMember}/packaging-data", cancellationToken);
        var envelope = await response.ReadEnvelopeAsync<List<PackagingDataRow>>(cancellationToken);

        Assert.Equal(2, envelope.Data.Count);
        Assert.All(envelope.Data, r => Assert.Equal(SchemeMember, r.OrganisationId));
        Assert.All(envelope.Data, r => Assert.Equal("2026-H1", r.SubmissionPeriod));
        Assert.Single(envelope.Data.Select(r => r.SubmissionId).Distinct());
    }

    [Fact]
    public async Task Packaging_data_json_has_no_nesting_and_no_row_id()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new ApiTestFactory();
        using var client = factory.CreateClient();

        var json = await client.GetStringAsync(
            $"/organisations/{DirectProducer}/packaging-data?year=2026", cancellationToken);

        // Asserted on the raw JSON rather than the model, so a nested or renamed field is caught even
        // if the C# type happens to still deserialise it.
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var data = doc.RootElement.GetProperty("data");
        Assert.Equal(System.Text.Json.JsonValueKind.Array, data.ValueKind);

        var first = data.EnumerateArray().First();
        Assert.True(first.TryGetProperty("organisationId", out _));
        Assert.True(first.TryGetProperty("submissionId", out _));
        Assert.False(first.TryGetProperty("organisation", out _));
        Assert.False(first.TryGetProperty("submissions", out _));
        Assert.False(first.TryGetProperty("packagingDataId", out _));
    }

    [Fact]
    public async Task Packaging_data_filters_by_year_and_status()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new ApiTestFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/organisations/{DirectProducer}/packaging-data?year=2025&status=rejected", cancellationToken);
        var envelope = await response.ReadEnvelopeAsync<List<PackagingDataRow>>(cancellationToken);

        Assert.NotEmpty(envelope.Data);
        Assert.All(envelope.Data, r => Assert.Equal("rejected", r.Status));
        Assert.Single(envelope.Data.Select(r => r.SubmissionId).Distinct());
        Assert.Contains(envelope.Data, r => r.SubsidiaryId == "100123-S01");
    }

    [Theory]
    [InlineData("year=1999", "Invalid year")]
    [InlineData("status=banana", "Invalid status")]
    public async Task Packaging_data_rejects_invalid_filters_with_problem_details(string query, string expectedTitle)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new ApiTestFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/organisations/{DirectProducer}/packaging-data?{query}", cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(ApiTestFactory.Json, cancellationToken);
        Assert.NotNull(problem);
        Assert.Equal(expectedTitle, problem.Title);
    }

    [Fact]
    public async Task Summary_totals_equal_the_sum_of_the_lines()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new ApiTestFactory();
        using var client = factory.CreateClient();

        // 100123's only 2026 submission is 2026-H1, so the report and summary cover the same rows.
        var report = await (await client.GetAsync(
                $"/organisations/{DirectProducer}/packaging-data?year=2026",
                cancellationToken))
            .ReadEnvelopeAsync<List<PackagingDataRow>>(cancellationToken);

        var summary = await (await client.GetAsync(
                $"/organisations/{DirectProducer}/packaging-data/summary?submissionPeriod=2026-H1",
                cancellationToken))
            .ReadEnvelopeAsync<PackagingDataSummary>(cancellationToken);

        // The guarantee a real implementation must also make: adding up the rows gives the summary
        // figure. Hardcoded fixture totals would quietly break this.
        var rows = report.Data;
        Assert.Equal(rows.Sum(r => r.PackagingMaterialWeight), summary.Data.Totals.Tonnage);
        Assert.Equal(rows.Count, summary.Data.Totals.LineCount);
        Assert.Equal(842.19m, summary.Data.Totals.Tonnage);
        Assert.Equal(PackagingMaterials.Plastic, summary.Data.ByMaterial.First().Key);
    }

    [Fact]
    public async Task An_invalid_submission_period_is_a_bad_request_not_an_empty_result()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new ApiTestFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/organisations/{DirectProducer}/packaging-data/summary?submissionPeriod=Q1-2026", cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(
            ApiTestFactory.Json, cancellationToken);
        Assert.NotNull(problem);
        Assert.Equal("Invalid submission period", problem.Title);
    }

    [Fact]
    public async Task Page_size_is_clamped_rather_than_rejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new ApiTestFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/organisations/{DirectProducer}/submissions?pageSize=99999", cancellationToken);
        var envelope = await response.ReadEnvelopeAsync<IReadOnlyCollection<SubmissionResponse>>(cancellationToken);

        Assert.NotNull(envelope.Meta.Page);
        Assert.Equal(PageRequest.MaxSize, envelope.Meta.Page.Size);
    }
}
