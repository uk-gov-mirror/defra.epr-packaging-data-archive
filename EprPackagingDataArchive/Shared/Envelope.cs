using System.Text.Json.Serialization;

namespace EprPackagingDataArchive.Shared;

/// <summary>
/// Every response is wrapped so that callers can see how fresh the data is and where it came from.
/// This exists from the first stub deliberately: when the source changes from fixtures to the
/// Common Data API, and later to a local projection, consumers are already handling staleness and
/// the contract does not change.
/// </summary>
public sealed record Envelope<T>
{
    public required T Data { get; init; }

    public required ResponseMeta Meta { get; init; }
}

public sealed record ResponseMeta
{
    /// <summary>When the underlying data was true, not when this response was generated.</summary>
    public required DateTimeOffset AsOf { get; init; }

    /// <summary>One of <see cref="DataSourceNames"/>. Tells a caller whether they are looking at real data.</summary>
    public required string Source { get; init; }

    /// <summary>Present only on paged collections. Omitted rather than sent as null everywhere else.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PageInfo? Page { get; init; }
}

public sealed record PageInfo
{
    public required int Number { get; init; }

    public required int Size { get; init; }

    /// <summary>Total matching records across all pages, not the count in this response.</summary>
    public required int Total { get; init; }
}

public static class DataSourceNames
{
    public const string Stub = "stub";
    public const string CommonDataApi = "common-data-api";
    public const string Projection = "projection";
}

public static class EnvelopeExtensions
{
    public static Envelope<T> InEnvelope<T>(this T data, DateTimeOffset asOf, string source, PageInfo? page = null) =>
        new() { Data = data, Meta = new ResponseMeta { AsOf = asOf, Source = source, Page = page } };
}
