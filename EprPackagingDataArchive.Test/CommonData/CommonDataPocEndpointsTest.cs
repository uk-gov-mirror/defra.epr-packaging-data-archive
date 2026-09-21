using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace EprPackagingDataArchive.Test.CommonData;

/// <summary>
/// The proof of concept routes pass warehouse responses through unmapped, and this service has no
/// authentication of its own. The property that matters most is therefore that they do not exist
/// unless deliberately switched on, which is what these tests pin.
///
/// Nothing here calls the Common Data API. Proving connectivity needs the real thing and credentials.
/// </summary>
public class CommonDataPocEndpointsTest
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/cd/diagnostics")]
    [InlineData("/cd/sync-time")]
    [InlineData("/cd/submissions?organisationReference=100123")]
    [InlineData("/cd/poms?relativeYear=2027")]
    public async Task Poc_routes_do_not_exist_by_default(string path)
    {
        await using var factory = new ApiTestFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path, Token);

        // Not mapped at all rather than mapped and refusing, so there is no surface to probe.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_v1_contract_is_unaffected_when_the_poc_is_disabled()
    {
        await using var factory = new ApiTestFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/organisations/100123", Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Diagnostics_reports_configuration_without_revealing_the_token()
    {
        await using var factory = new EnabledPocFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/cd/diagnostics", Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(Token);

        Assert.Contains("https://example.invalid", body);
        // Presence is reported, the value never is.
        Assert.Contains("authTokenConfigured", body);
        Assert.DoesNotContain("super-secret-token", body);
    }

    [Fact]
    public async Task Poc_routes_are_absent_from_the_openapi_contract_even_when_enabled()
    {
        await using var factory = new EnabledPocFactory();
        using var client = factory.CreateClient();

        var spec = await client.GetStringAsync("/openapi/v1.json", Token);

        // Exploratory routes must not reach a consumer's generated client.
        Assert.DoesNotContain("/cd/", spec);
        Assert.Contains("/organisations/", spec);
    }

    [Fact]
    public async Task Submissions_requires_an_organisation_reference()
    {
        await using var factory = new EnabledPocFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/cd/submissions", Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/cd/poms")]
    [InlineData("/cd/poms?relativeYear=1999")]
    public async Task Poms_requires_a_plausible_relative_year(string path)
    {
        await using var factory = new EnabledPocFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path, Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Boots the app with the proof of concept switched on and pointed at an unroutable host.</summary>
    private sealed class EnabledPocFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["CommonDataApi:Enabled"] = "true",
                    ["CommonDataApi:BaseUrl"] = "https://example.invalid",
                    ["CommonDataApi:AuthToken"] = "super-secret-token"
                }));
    }
}
