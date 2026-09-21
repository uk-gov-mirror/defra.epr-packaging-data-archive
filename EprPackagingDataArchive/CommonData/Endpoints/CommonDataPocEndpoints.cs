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
    /// <summary>Rows returned by <c>/cd/poms</c> when no <c>limit</c> is given.</summary>
    public const int DefaultLimit = 100;

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
                "GET /cd/poms?year={yyyy}&limit={n}"
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
        [FromQuery] int? year,
        [FromQuery] int? limit,
        [FromServices] ICommonDataApiClient client,
        CancellationToken cancellationToken)
    {
        if (year is null or < 2020 or > 2100)
        {
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "year is required",
                Detail = "Provide the four digit year the packaging data is for, for example ?year=2024, "
                         + "the same as ?year= on /organisations/{organisationId}/packaging-data.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        // Same year as the product endpoints, so the probe and the contract never disagree about
        // what 2024 means. Upstream is PayCal's stream, which speaks in relative years: the year
        // fees are charged for, one after the data. The upstream URL in the response shows it.
        return TypedResults.Ok(await client.GetPomSampleAsync(
            year.Value + 1, limit ?? DefaultLimit, cancellationToken));
    }
}
