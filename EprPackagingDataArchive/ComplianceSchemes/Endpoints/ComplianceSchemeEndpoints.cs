using EprPackagingDataArchive.ComplianceSchemes.Models;
using EprPackagingDataArchive.ComplianceSchemes.Providers;
using EprPackagingDataArchive.PackagingData.Models;
using EprPackagingDataArchive.PackagingData.Providers;
using EprPackagingDataArchive.Shared;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace EprPackagingDataArchive.ComplianceSchemes.Endpoints;

/// <summary>
/// A compliance scheme fans out across its members.
///
/// This is the second root rather than a variant of the organisation endpoints, because "packaging
/// data for a scheme" means the aggregate of its members' data, which is a different question from
/// "packaging data for an organisation".
/// </summary>
public static class ComplianceSchemeEndpoints
{
    public static RouteGroupBuilder MapComplianceSchemeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/compliance-schemes").WithTags("Compliance schemes");

        group.MapGet("/{schemeId}/members", GetMembers)
            .WithName("GetSchemeMembers")
            .WithSummary("List producers registered through this scheme.");

        group.MapGet("/{schemeId}/packaging-data/summary", GetPackagingDataSummary)
            .WithName("GetSchemePackagingDataSummary")
            .WithSummary("Aggregated tonnage across every member of this scheme.");

        group.MapGet("/{schemeId}/reporting-status", GetReportingStatus)
            .WithName("GetSchemeReportingStatus")
            .WithSummary("Which members have reported for a period and which have not.");

        return group;
    }

    private static async Task<Results<Ok<Envelope<IReadOnlyCollection<SchemeMember>>>, NotFound>> GetMembers(
        [FromRoute] string schemeId,
        [FromQuery] DateOnly? asAt,
        [FromQuery] string? status,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromServices] IComplianceSchemeProvider schemes,
        [FromServices] IDataSourceDescriptor source,
        CancellationToken cancellationToken)
    {
        if (!await schemes.ExistsAsync(schemeId, cancellationToken)) return TypedResults.NotFound();

        var all = await schemes.GetMembersAsync(
            schemeId,
            new MemberQuery { AsAt = asAt, Status = status },
            cancellationToken);

        var request = PageRequest.From(page, pageSize);

        return TypedResults.Ok(request.Apply(all).InEnvelope(
            source.AsOf,
            source.Name,
            new PageInfo { Number = request.Number, Size = request.Size, Total = all.Count }));
    }

    private static async Task<Results<Ok<Envelope<SchemePackagingDataSummary>>, NotFound, BadRequest<ProblemDetails>>>
        GetPackagingDataSummary(
            [FromRoute] string schemeId,
            [FromQuery] string? submissionPeriod,
            [FromQuery] int? obligationYear,
            [FromQuery] string? material,
            [FromServices] IComplianceSchemeProvider schemes,
            [FromServices] IPackagingDataProvider packagingData,
            [FromServices] IDataSourceDescriptor source,
            CancellationToken cancellationToken)
    {
        if (PeriodProblem(submissionPeriod) is { } problem) return TypedResults.BadRequest(problem);

        if (!await schemes.ExistsAsync(schemeId, cancellationToken)) return TypedResults.NotFound();

        var summary = await packagingData.GetSchemeSummaryAsync(
            schemeId,
            new PackagingDataQuery
            {
                SubmissionPeriod = submissionPeriod,
                ObligationYear = obligationYear,
                Material = material
            },
            cancellationToken);

        return TypedResults.Ok(summary.InEnvelope(source.AsOf, source.Name));
    }

    private static async Task<Results<Ok<Envelope<SchemeReportingStatus>>, NotFound, BadRequest<ProblemDetails>>>
        GetReportingStatus(
            [FromRoute] string schemeId,
            [FromQuery] string submissionPeriod,
            [FromServices] IComplianceSchemeProvider schemes,
            [FromServices] IDataSourceDescriptor source,
            CancellationToken cancellationToken)
    {
        // Required here rather than optional: "who has not reported" is meaningless without a period.
        if (PeriodProblem(submissionPeriod, required: true) is { } problem) return TypedResults.BadRequest(problem);

        if (!await schemes.ExistsAsync(schemeId, cancellationToken)) return TypedResults.NotFound();

        var status = await schemes.GetReportingStatusAsync(schemeId, submissionPeriod, cancellationToken);

        return TypedResults.Ok(status.InEnvelope(source.AsOf, source.Name));
    }

    private static ProblemDetails? PeriodProblem(string? submissionPeriod, bool required = false)
    {
        if (string.IsNullOrWhiteSpace(submissionPeriod))
        {
            return required
                ? new ProblemDetails
                {
                    Title = "Submission period is required",
                    Detail = "Provide a submissionPeriod, for example 2026-H1.",
                    Status = StatusCodes.Status400BadRequest
                }
                : null;
        }

        if (SubmissionPeriod.TryParse(submissionPeriod, out _)) return null;

        return new ProblemDetails
        {
            Title = "Invalid submission period",
            Detail = $"'{submissionPeriod}' is not a submission period. Expected a year and a period, "
                     + "for example 2026-H1. Valid periods are H1, H2 and P0.",
            Status = StatusCodes.Status400BadRequest
        };
    }
}
