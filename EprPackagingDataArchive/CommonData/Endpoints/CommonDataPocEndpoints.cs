using EprPackagingDataArchive.CommonData.Client;
using EprPackagingDataArchive.Config;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace EprPackagingDataArchive.CommonData.Endpoints;

/// <summary>
/// Proof of concept routes for reaching the Azure Common Data API.
///
/// Deliberately outside the service contract and excluded from the OpenAPI document, because these are not part of
/// the product contract and should not appear in a consumer's generated client. They pass warehouse
/// responses through unmapped, so that what the data really looks like can be seen before deciding
/// how to present it.
///
/// Mapped only when CommonDataApi:Enabled is true. This service has no authentication of its own, so
/// leaving these permanently on would be an unauthenticated window onto producer data.
/// </summary>
public static class CommonDataPocEndpoints
{
    public static RouteGroupBuilder MapCommonDataPocEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/cd").ExcludeFromDescription();

        group.MapGet("/diagnostics", Diagnostics);
        group.MapGet("/sync-time", SyncTime);
        group.MapGet("/submissions", Submissions);
        group.MapGet("/poms", Poms);

        return group;
    }

    /// <summary>
    /// Answers "is this thing even pointed at the right place" without making a call. Reports no
    /// secret values, only whether they are present.
    /// </summary>
    private static Ok<object> Diagnostics([FromServices] IOptions<CommonDataApiOptions> options)
    {
        var o = options.Value;

        return TypedResults.Ok<object>(new
        {
            enabled = o.Enabled,
            baseUrl = string.IsNullOrWhiteSpace(o.BaseUrl) ? "(not configured)" : o.BaseUrl,
            timeoutSeconds = o.TimeoutSeconds,
            maxStreamRows = o.MaxStreamRows,
            authTokenConfigured = !string.IsNullOrWhiteSpace(o.AuthToken),
            httpProxyDetected = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HTTP_PROXY")),
            routes = new[]
            {
                "GET /cd/sync-time",
                "GET /cd/submissions?organisationReference={ref}&pageSize={n}",
                "GET /cd/poms?relativeYear={yyyy}&take={n}"
            },
            note = "Proof of concept. Responses are passed through from the Common Data API unmapped."
        });
    }

    private static async Task<Ok<UpstreamResult>> SyncTime(
        [FromServices] ICommonDataApiClient client,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await client.GetLastSyncTimeAsync(cancellationToken));

    private static async Task<Results<Ok<UpstreamResult>, BadRequest<ProblemDetails>>> Submissions(
        [FromQuery] string? organisationReference,
        [FromQuery] int? pageSize,
        [FromServices] ICommonDataApiClient client,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(organisationReference))
        {
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "organisationReference is required",
                Detail = "Provide the EPR organisation reference to filter on, for example ?organisationReference=100123.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        return TypedResults.Ok(await client.GetPomSummaryAsync(
            organisationReference, Math.Clamp(pageSize ?? 10, 1, 100), cancellationToken));
    }

    private static async Task<Results<Ok<UpstreamResult>, BadRequest<ProblemDetails>>> Poms(
        [FromQuery] int? relativeYear,
        [FromQuery] int? take,
        [FromServices] ICommonDataApiClient client,
        CancellationToken cancellationToken)
    {
        if (relativeYear is null or < 2020 or > 2100)
        {
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "relativeYear is required",
                Detail = "Provide a four digit PayCal relative year, for example ?relativeYear=2027. "
                         + "Upstream returns POM rows for the submission year one prior to it.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        return TypedResults.Ok(await client.GetPomSampleAsync(
            relativeYear.Value, take ?? 25, cancellationToken));
    }
}
