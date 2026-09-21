using EprPackagingDataArchive.Organisations.Models;
using EprPackagingDataArchive.Organisations.Providers;
using EprPackagingDataArchive.PackagingData.Models;
using EprPackagingDataArchive.PackagingData.Providers;
using EprPackagingDataArchive.Shared;
using EprPackagingDataArchive.Utils.Auditing;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace EprPackagingDataArchive.Organisations.Endpoints;

/// <summary>
/// Everything hanging off a single organisation.
///
/// The route always names the organisation the data is ABOUT. Whether that organisation reported for
/// itself or had a compliance scheme report on its behalf shows up in the payload as
/// <c>submittedBy</c>, never as a different endpoint or a different shape.
/// </summary>
public static class OrganisationEndpoints
{
    public static RouteGroupBuilder MapOrganisationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/organisations").WithTags("Organisations");

        group.MapGet("/{organisationId}", GetOrganisation)
            .WithName("GetOrganisation")
            .WithSummary("Get an organisation by its EPR reference number.");

        group.MapGet("/{organisationId}/submissions", GetSubmissions)
            .WithName("GetOrganisationSubmissions")
            .WithSummary("List submissions made for this organisation, newest first.");

        group.MapGet("/{organisationId}/submissions/{submissionId}", GetSubmission)
            .WithName("GetOrganisationSubmission")
            .WithSummary("Get one submission, including validation counts and warehouse availability.");

        group.MapGet("/{organisationId}/packaging-data", GetPackagingData)
            .WithName("GetOrganisationPackagingData")
            .WithSummary("The organisation's packaging data as flat rows, each carrying its submission, "
                         + "filterable by year and submission status.");

        group.MapGet("/{organisationId}/packaging-data/summary", GetPackagingDataSummary)
            .WithName("GetOrganisationPackagingDataSummary")
            .WithSummary("Aggregated tonnage for this organisation, broken down by material, activity and nation.");

        return group;
    }

    private static async Task<Results<Ok<Envelope<OrganisationResponse>>, NotFound>> GetOrganisation(
        [FromRoute] string organisationId,
        [FromServices] IOrganisationProvider organisations,
        [FromServices] IDataSourceDescriptor source,
        CancellationToken cancellationToken)
    {
        var organisation = await organisations.GetOrganisationAsync(organisationId, cancellationToken);

        return organisation is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(organisation.InEnvelope(source.AsOf, source.Name));
    }

    private static async Task<Results<Ok<Envelope<IReadOnlyCollection<SubmissionResponse>>>, NotFound, BadRequest<ProblemDetails>>>
        GetSubmissions(
            [FromRoute] string organisationId,
            [FromQuery] string? submissionPeriod,
            [FromQuery] int? obligationYear,
            [FromQuery] string? type,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            [FromServices] IOrganisationProvider organisations,
            [FromServices] IDataSourceDescriptor source,
            CancellationToken cancellationToken)
    {
        if (InvalidPeriod(submissionPeriod, out var problem)) return TypedResults.BadRequest(problem);

        if (!await organisations.ExistsAsync(organisationId, cancellationToken)) return TypedResults.NotFound();

        var all = await organisations.GetSubmissionsAsync(
            organisationId,
            new SubmissionQuery
            {
                SubmissionPeriod = submissionPeriod,
                ObligationYear = obligationYear,
                Type = type
            },
            cancellationToken);

        return TypedResults.Ok(Paged(all, page, pageSize, source));
    }

    private static async Task<Results<Ok<Envelope<SubmissionDetailResponse>>, NotFound>> GetSubmission(
        [FromRoute] string organisationId,
        [FromRoute] string submissionId,
        [FromServices] IOrganisationProvider organisations,
        [FromServices] IDataSourceDescriptor source,
        CancellationToken cancellationToken)
    {
        var submission = await organisations.GetSubmissionAsync(organisationId, submissionId, cancellationToken);

        return submission is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(submission.InEnvelope(source.AsOf, source.Name));
    }

    private static async Task<Results<Ok<Envelope<IReadOnlyCollection<PackagingDataRow>>>, NotFound, BadRequest<ProblemDetails>>>
        GetPackagingData(
            [FromRoute] string organisationId,
            [FromQuery] int? year,
            [FromQuery] string? status,
            [FromServices] IPackagingDataProvider packagingData,
            [FromServices] IDataSourceDescriptor source,
            [FromServices] ILoggerFactory loggerFactory,
            CancellationToken cancellationToken)
    {
        if (year is < 2020 or > 2100)
        {
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Invalid year",
                Detail = $"'{year}' is not a valid submission year. Provide a four digit year, for example 2025.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (status is not null && !status.Equals("accepted", StringComparison.OrdinalIgnoreCase)
                               && !status.Equals("rejected", StringComparison.OrdinalIgnoreCase))
        {
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Invalid status",
                Detail = $"'{status}' is not a recognised status filter. Valid values are accepted and rejected.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var rows = await packagingData.GetReportAsync(
            organisationId, new ReportQuery { Year = year, Status = status }, cancellationToken);

        if (rows is null) return TypedResults.NotFound();

        // One audit event per read, routed to the CDP audit store. A deliberate starting point for
        // the ticket's open "Audit?" question rather than settled policy; see DECISIONS.md 7.
        loggerFactory.CreateLogger("PackagingData.Report").Audit(
            "Packaging data report requested for organisation {OrganisationId} (year: {Year}, status: {Status})",
            organisationId, year, status ?? "any");

        return TypedResults.Ok(rows.InEnvelope(source.AsOf, source.Name));
    }

    private static async Task<Results<Ok<Envelope<PackagingDataSummary>>, NotFound, BadRequest<ProblemDetails>>>
        GetPackagingDataSummary(
            [FromRoute] string organisationId,
            [FromQuery] string? submissionPeriod,
            [FromQuery] int? obligationYear,
            [FromQuery] string? material,
            [FromQuery] string? submittedBy,
            [FromServices] IOrganisationProvider organisations,
            [FromServices] IPackagingDataProvider packagingData,
            [FromServices] IDataSourceDescriptor source,
            CancellationToken cancellationToken)
    {
        if (InvalidPeriod(submissionPeriod, out var problem)) return TypedResults.BadRequest(problem);

        if (!await organisations.ExistsAsync(organisationId, cancellationToken)) return TypedResults.NotFound();

        var summary = await packagingData.GetSummaryAsync(
            organisationId,
            QueryFrom(submissionPeriod, obligationYear, material, submittedBy),
            cancellationToken);

        return TypedResults.Ok(summary.InEnvelope(source.AsOf, source.Name));
    }

    private static PackagingDataQuery QueryFrom(
        string? submissionPeriod, int? obligationYear, string? material, string? submittedBy) =>
        new()
        {
            SubmissionPeriod = submissionPeriod,
            ObligationYear = obligationYear,
            Material = material,
            SubmittedBy = submittedBy
        };

    private static Envelope<IReadOnlyCollection<T>> Paged<T>(
        IReadOnlyCollection<T> all, int? page, int? pageSize, IDataSourceDescriptor source)
    {
        var request = PageRequest.From(page, pageSize);

        return request.Apply(all).InEnvelope(
            source.AsOf,
            source.Name,
            new PageInfo { Number = request.Number, Size = request.Size, Total = all.Count });
    }

    /// <summary>
    /// Periods are validated at the edge so a caller gets a 400 explaining the format rather than an
    /// empty result set that looks like "this organisation reported nothing".
    /// </summary>
    private static bool InvalidPeriod(string? submissionPeriod, out ProblemDetails problem)
    {
        problem = default!;

        if (submissionPeriod is null || Shared.SubmissionPeriod.TryParse(submissionPeriod, out _)) return false;

        problem = new ProblemDetails
        {
            Title = "Invalid submission period",
            Detail = $"'{submissionPeriod}' is not a submission period. Expected a year and a period, "
                     + "for example 2026-H1. Valid periods are H1, H2 and P0.",
            Status = StatusCodes.Status400BadRequest
        };

        return true;
    }
}
