using EprPackagingDataArchive.CommonData;
using EprPackagingDataArchive.CommonData.Endpoints;
using EprPackagingDataArchive.ComplianceSchemes.Endpoints;
using EprPackagingDataArchive.Organisations.Endpoints;
using EprPackagingDataArchive.Shared;
using EprPackagingDataArchive.Utils;
using EprPackagingDataArchive.Utils.Http;
using System.Diagnostics.CodeAnalysis;
using EprPackagingDataArchive.Utils.Logging;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;

var app = BuildApp(args);
await app.RunAsync();

[ExcludeFromCodeCoverage]
static WebApplication BuildApp(string[] args)
{
    var builder = WebApplication.CreateBuilder(args);

    ConfigureHost(builder);
    ConfigureServices(builder);

    var app = builder.Build();

    ConfigureMiddleware(app);
    ConfigureEndpoints(app);

    return app;
}

[ExcludeFromCodeCoverage]
static void ConfigureHost(WebApplicationBuilder builder)
{
    builder.Host.UseSerilog(CdpLogging.Configuration);
}

[ExcludeFromCodeCoverage]
static void ConfigureServices(WebApplicationBuilder builder)
{
    var services = builder.Services;
    var configuration = builder.Configuration;

    // Trust material must be loaded before anything creates outbound connections.
    services.LoadCustomTrustStoreFromEnvironment();

    services.AddProblemDetails();
    services.AddValidation();
    services.AddOpenApi();

    services.AddHttpContextAccessor();
    services.AddSingleton(TimeProvider.System);

    ConfigureHeaderPropagation(services, configuration);
    ConfigureHttpClients(services);

    services.AddHealthChecks();

    // Selects which adapters back the provider interfaces. Phase one registers the stubs.
    services.AddPackagingDataProviders(configuration);

    // Proof of concept only, off unless CommonDataApi:Enabled is true.
    services.AddCommonDataPoc(configuration);

    // Mongo is deliberately not registered. This service holds no data of its own yet, and the
    // choice between MongoDB and Aurora PostgreSQL is still open. The wiring in Utils/Mongo is
    // intact, so re-enabling it is a single ConfigureMongo call here once that decision is made.
}

[ExcludeFromCodeCoverage]
static void ConfigureHeaderPropagation(IServiceCollection services, IConfiguration configuration)
{
    var traceHeader = configuration.GetValue<string>("TraceHeader");

    services.AddHeaderPropagation(options =>
    {
        if (!string.IsNullOrWhiteSpace(traceHeader))
        {
            options.Headers.Add(traceHeader);
        }
    });
}

[ExcludeFromCodeCoverage]
static void ConfigureHttpClients(IServiceCollection services)
{
    services.AddTransient<ProxyHttpMessageHandler>();

    // Phase two registers the Common Data API client here. It must go through
    // AddHttpClientWithTracingAndProxy: a plain AddHttpClient loses both the x-cdp-request-id
    // propagation and the authenticated CDP egress proxy.
}

[ExcludeFromCodeCoverage]
static void ConfigureMiddleware(WebApplication app)
{
    app.UseSerilogRequestLogging();

    app.UseHeaderPropagation();
}

[ExcludeFromCodeCoverage]
static void ConfigureEndpoints(WebApplication app)
{
    app.MapHealthChecks("/health", new HealthCheckOptions());

    // The contract is the deliverable in phase one, so the OpenAPI document is served in every
    // environment rather than gated on Development. There is no data behind it yet.
    app.MapOpenApi();

    app.MapOrganisationEndpoints();
    app.MapComplianceSchemeEndpoints();

    // Exploratory routes onto the Azure warehouse. Not part of the service contract, excluded from the
    // OpenAPI document, and not mapped at all unless explicitly enabled.
    if (CommonDataRegistration.IsEnabled(app.Configuration))
    {
        app.MapCommonDataPocEndpoints();
    }
}
