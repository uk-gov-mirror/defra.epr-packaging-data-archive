using System.Text.Json.Nodes;
using EprPackagingDataArchive.CommonData.Client;
using EprPackagingDataArchive.Shared;

namespace EprPackagingDataArchive.CommonData;

/// <summary>
/// Reports how fresh the upstream data is, by asking the Common Data API when Synapse last synced.
///
/// <see cref="IDataSourceDescriptor.AsOf"/> is a synchronous property, read while a response is
/// being built, so the value is cached here and refreshed by <see cref="EnsureFreshAsync"/> from
/// inside a request. Reading TimeProvider instead would be simpler and would be a lie: it would
/// report when the response was built, not when the data was true.
///
/// The refresh deliberately does NOT run on a background timer. Outbound clients in this service go
/// through AddHttpClientWithTracingAndProxy, which propagates the x-cdp-request-id header, and
/// header propagation throws outside an HTTP request context. Refreshing on the request path keeps
/// one HTTP client configuration for everything rather than a second, untraced one.
/// </summary>
public sealed class CommonDataApiDataSourceDescriptor
{
    private static readonly TimeSpan RefreshAfter = TimeSpan.FromMinutes(15);
    private readonly SemaphoreSlim _gate = new(1, 1);

    private DateTimeOffset? _asOf;
    private DateTimeOffset _lastChecked = DateTimeOffset.MinValue;

    /// <summary>
    /// Falls back to the Unix epoch until the first successful read. A visibly wrong date is better
    /// than a plausible one: a consumer can spot the epoch, whereas "now" would look correct and
    /// quietly claim the data is current when we have not been told that it is.
    /// </summary>
    public DateTimeOffset AsOf => _asOf ?? DateTimeOffset.UnixEpoch;

    public async Task EnsureFreshAsync(
        ICommonDataApiClient client, TimeProvider time, ILogger logger, CancellationToken cancellationToken)
    {
        if (time.GetUtcNow() - _lastChecked < RefreshAfter) return;

        if (!await _gate.WaitAsync(TimeSpan.Zero, cancellationToken)) return;

        try
        {
            if (time.GetUtcNow() - _lastChecked < RefreshAfter) return;

            // Marked as checked whatever happens, so a persistently failing upstream is asked once
            // every fifteen minutes rather than on every single request.
            _lastChecked = time.GetUtcNow();

            var result = await client.GetLastSyncTimeAsync(cancellationToken);

            if (result.Payload is not null && TryReadTimestamp(result.Payload, out var asOf))
            {
                _asOf = asOf;
                logger.LogInformation("Upstream data is current as of {AsOf}", asOf);
                return;
            }

            logger.LogWarning(
                "Could not read the upstream sync time. Status={Status} Error={Error}",
                result.Upstream.Status, result.Upstream.Error);
        }
        catch (Exception ex)
        {
            // Never fails the request. Not knowing the sync time makes meta.asOf stale, which is
            // visible; failing the call would lose data the caller can perfectly well have.
            logger.LogWarning(ex, "Could not read the upstream sync time");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Takes the first value in the payload that parses as a date, rather than binding a field name.
    /// The upstream response shape is not part of any contract we control.
    /// </summary>
    private static bool TryReadTimestamp(JsonNode payload, out DateTimeOffset value)
    {
        value = default;

        var candidates = payload switch
        {
            JsonObject o => o.Select(pair => pair.Value),
            JsonArray a => a.AsEnumerable(),
            _ => [payload]
        };

        foreach (var candidate in candidates.Append(payload))
        {
            if (candidate is null) continue;

            if (DateTimeOffset.TryParse(candidate.ToString(), out value)) return true;
        }

        return false;
    }
}

/// <summary>Adapts the cached value onto the port the endpoints depend on.</summary>
public sealed class CommonDataApiSourceDescriptor(CommonDataApiDataSourceDescriptor state) : IDataSourceDescriptor
{
    public string Name => DataSourceNames.CommonDataApi;

    public DateTimeOffset AsOf => state.AsOf;
}
