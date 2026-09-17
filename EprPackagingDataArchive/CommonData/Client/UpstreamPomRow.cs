namespace EprPackagingDataArchive.CommonData.Client;

/// <summary>
/// One row as the Common Data API returns it from GET /api/pom/{organisationId}.
///
/// This is the upstream vocabulary, not ours. It stops at the edge: the provider maps it onto the
/// domain model and nothing outside this namespace sees these names. Keeping the two apart is what
/// lets the contract survive an upstream change.
/// </summary>
public sealed record UpstreamPomRow
{
    public int OrganisationId { get; init; }
    public string? OrganisationName { get; init; }
    public string? SubmissionPeriod { get; init; }
    public string? SubmissionPeriodDescription { get; init; }
    public string? SubsidiaryId { get; init; }
    public string? PackagingType { get; init; }
    public string? PackagingMaterial { get; init; }
    public string? PackagingMaterialSubtype { get; init; }
    public double? PackagingMaterialWeight { get; init; }
    public string? PackagingClass { get; init; }
    public string? PackagingActivity { get; init; }
    public string? FromCountry { get; init; }
    public string? SubmitterId { get; init; }
    public string? RamRagRating { get; init; }
}
