namespace EprPackagingDataArchive.Config;

/// <summary>
/// Settings for the Common Data API proof of concept.
///
/// This is exploratory, not part of the product contract. It exists to answer one question: can this
/// service reach the Azure warehouse API from where it runs, and what does the data actually look
/// like? It is disabled unless switched on, because this service has no authentication of its own
/// and these routes pass warehouse responses straight through.
/// </summary>
public class CommonDataApiOptions
{
    public const string SectionName = "CommonDataApi";

    /// <summary>Off unless explicitly enabled. When false the /cd routes are not mapped at all.</summary>
    public bool Enabled { get; init; }

    /// <summary>For example https://devrwdwebwa9415.azurewebsites.net</summary>
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>The warehouse is slow. Existing callers in the estate allow 120 seconds.</summary>
    public int TimeoutSeconds { get; init; } = 120;

    /// <summary>
    /// Optional bearer token. Existing Azure callers reach this API with no authentication at all,
    /// relying on network isolation, so this is left empty by default. It is here so that a token
    /// can be supplied from configuration once the auth story is confirmed, without a code change.
    /// </summary>
    public string? AuthToken { get; init; }

    /// <summary>
    /// Ceiling on the <c>limit</c> a caller can ask for when sampling the POM stream. That endpoint
    /// returns a whole year for every producer and is rate limited to one caller, which here means
    /// dev9 PayCal, so a ceiling stops a stray <c>limit</c> from holding the slot for a full year.
    /// </summary>
    public int MaxStreamRows { get; init; } = 10_000;
}
