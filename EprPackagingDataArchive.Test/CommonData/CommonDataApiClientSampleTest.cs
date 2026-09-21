using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using EprPackagingDataArchive.CommonData.Client;
using EprPackagingDataArchive.Config;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EprPackagingDataArchive.Test.CommonData;

/// <summary>
/// The POM sampler reads a stream that has no count, so the only way a caller learns whether they
/// have everything is <c>hasMore</c>. These tests pin that it is right at the edges: exactly at the
/// limit, one past it, and with blank lines in the stream.
/// </summary>
public class CommonDataApiClientSampleTest
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Returns_the_requested_number_of_rows_and_says_there_are_more()
    {
        var payload = await SampleAsync(rowsUpstream: 10, limit: 3);

        Assert.Equal(3, payload["returned"]!.GetValue<int>());
        Assert.Equal(3, payload["rows"]!.AsArray().Count);
        Assert.True(payload["hasMore"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Has_no_more_when_the_stream_ends_exactly_at_the_limit()
    {
        var payload = await SampleAsync(rowsUpstream: 3, limit: 3);

        Assert.Equal(3, payload["returned"]!.GetValue<int>());
        Assert.False(payload["hasMore"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Has_no_more_when_the_stream_ends_before_the_limit()
    {
        var payload = await SampleAsync(rowsUpstream: 2, limit: 5);

        Assert.Equal(2, payload["returned"]!.GetValue<int>());
        Assert.False(payload["hasMore"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Trailing_blank_lines_do_not_count_as_more_rows()
    {
        var payload = await SampleAsync(rowsUpstream: 3, limit: 3, trailer: "\n\n  \n");

        Assert.False(payload["hasMore"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_limit_above_the_ceiling_is_capped_and_says_so()
    {
        var payload = await SampleAsync(rowsUpstream: 10, limit: 500, ceiling: 4);

        Assert.Equal(500, payload["limitRequested"]!.GetValue<int>());
        Assert.Equal(4, payload["limit"]!.GetValue<int>());
        Assert.Equal(4, payload["returned"]!.GetValue<int>());
        Assert.True(payload["hasMore"]!.GetValue<bool>());
    }

    [Fact]
    public void The_default_ceiling_is_ten_thousand() =>
        Assert.Equal(10_000, new CommonDataApiOptions().MaxStreamRows);

    private static async Task<JsonObject> SampleAsync(int rowsUpstream, int limit, int ceiling = 10_000, string trailer = "")
    {
        var ndjson = new StringBuilder();
        for (var i = 0; i < rowsUpstream; i++)
        {
            ndjson.Append($"{{\"organisationId\":{100000 + i}}}\n");
        }
        ndjson.Append(trailer);

        using var http = new HttpClient(new FixedResponseHandler(ndjson.ToString()))
        {
            BaseAddress = new Uri("https://example.invalid/")
        };

        var client = new CommonDataApiClient(
            http,
            Options.Create(new CommonDataApiOptions { BaseUrl = "https://example.invalid", MaxStreamRows = ceiling }),
            NullLogger<CommonDataApiClient>.Instance);

        var result = await client.GetPomSampleAsync(2025, limit, Token);

        Assert.Equal(200, result.Upstream.Status);
        return result.Payload!.AsObject();
    }

    private sealed class FixedResponseHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/x-ndjson")
            });
    }
}
