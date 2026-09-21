namespace EprPackagingDataArchive.CommonData.Client;

/// <summary>
/// Calls the Azure Common Data API directly, passing responses through unmapped.
///
/// Deliberately not one of the domain providers. Mapping upstream rows onto the domain model is a
/// later decision, and this exists to see the raw shape before making it.
/// </summary>
public interface ICommonDataApiClient
{
    /// <summary>
    /// The smoke test. Takes no parameters and returns a timestamp, so a 200 here means the whole
    /// chain works: egress, firewall, TLS and auth. Start with this one.
    /// </summary>
    Task<UpstreamResult> GetLastSyncTimeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The only organisation-filterable endpoint upstream. Built as the regulator's caseworker grid,
    /// so it returns submission summaries rather than packaging lines.
    /// </summary>
    Task<UpstreamResult> GetPomSummaryAsync(string organisationReference, int pageSize, CancellationToken cancellationToken);

    /// <summary>
    /// Samples the POM stream. That endpoint returns NDJSON for every producer across a whole year
    /// with no organisation filter, so this reads only the first <paramref name="limit"/> rows, plus
    /// one more to learn whether there are others, and then stops, which also releases the upstream
    /// rate limit slot.
    /// </summary>
    Task<UpstreamResult> GetPomSampleAsync(int relativeYear, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Packaging rows for a single organisation, typed rather than passed through raw.
    ///
    /// Unlike the three methods above, this one backs a real endpoint rather than exploration, so it
    /// returns a mapped shape and throws on failure instead of reporting the failure as data. A
    /// provider needs to be able to tell "no rows" from "upstream was unreachable"; UpstreamResult
    /// deliberately blurs that, which is right for a probe and wrong here.
    /// </summary>
    /// <param name="relativeYear">
    /// Upstream's year convention, which is the submission year plus one. Null for every year.
    /// </param>
    Task<IReadOnlyCollection<UpstreamPomRow>> GetOrganisationPomsAsync(
        int organisationId, int? relativeYear, CancellationToken cancellationToken);
}
