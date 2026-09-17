using System.Diagnostics.CodeAnalysis;
using EprPackagingDataArchive.CommonData;
using EprPackagingDataArchive.ComplianceSchemes.Providers;
using EprPackagingDataArchive.Config;
using EprPackagingDataArchive.Organisations.Providers;
using EprPackagingDataArchive.PackagingData.Providers;
using Microsoft.Extensions.Options;

namespace EprPackagingDataArchive.Shared;

/// <summary>
/// The one place that decides which adapters back the API.
///
/// Endpoints depend only on the provider interfaces, so moving from fixtures to the Common Data API
/// and later to a local projection is a change here and nowhere else.
/// </summary>
[ExcludeFromCodeCoverage]
public static class DataSourceRegistration
{
    public static IServiceCollection AddPackagingDataProviders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<DataSourceOptions>()
            .Bind(configuration.GetSection(DataSourceOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var mode = configuration
            .GetSection(DataSourceOptions.SectionName)
            .Get<DataSourceOptions>()?.Mode ?? DataSourceMode.Stub;

        return mode switch
        {
            DataSourceMode.Stub => services.AddStubProviders(),

            // Phase two. The adapters call the Azure Common Data API through the CDP egress proxy.
            // The client itself is registered by AddCommonDataPoc using
            // AddHttpClientWithTracingAndProxy rather than AddHttpClient, so it keeps both request
            // correlation and the proxy credentials.
            //
            // Deploying in this mode still needs a Squid egress allow-list entry and the Azure
            // resource firewall opened to CDP's egress IPs. Locally it needs the Azure VPN.
            DataSourceMode.CommonDataApi => services.AddCommonDataApiProviders(),

            // Phase three, once the team has chosen between MongoDB and Aurora PostgreSQL.
            DataSourceMode.Projection => throw new NotSupportedException(
                "DataSource:Mode=Projection is not implemented yet. It arrives in phase three, "
                + "once a persistent store is chosen. Use Stub until then."),

            _ => throw new ArgumentOutOfRangeException(
                nameof(configuration), mode, "Unrecognised data source mode")
        };
    }

    /// <summary>
    /// Packaging data comes from upstream; organisations and compliance schemes do not, because
    /// there is no upstream endpoint for them yet. Those two keep their stub providers so that the
    /// endpoints depending on them still answer, and every response still names its own source
    /// through the envelope's meta.source.
    /// </summary>
    private static IServiceCollection AddCommonDataApiProviders(this IServiceCollection services)
    {
        // A base URL is optional for the /cd probe routes, which simply are not mapped without one,
        // but it is mandatory here: with no base address every request would fail at the first call
        // rather than at startup. Same reasoning as the other options in this codebase.
        services
            .AddOptions<CommonDataApiOptions>()
            .Validate(o => !string.IsNullOrWhiteSpace(o.BaseUrl),
                "CommonDataApi:BaseUrl is required when DataSource:Mode is CommonDataApi.")
            .ValidateOnStart();

        // Singleton so the cached sync time survives across requests; the provider itself is
        // scoped because its HttpClient is.
        services.AddSingleton<CommonDataApiDataSourceDescriptor>();
        services.AddSingleton<IDataSourceDescriptor, CommonDataApiSourceDescriptor>();

        services.AddScoped<IPackagingDataProvider, CommonDataApiPackagingDataProvider>();

        services.AddSingleton<IOrganisationProvider, StubOrganisationProvider>();
        services.AddSingleton<IComplianceSchemeProvider, StubComplianceSchemeProvider>();

        return services;
    }

    private static IServiceCollection AddStubProviders(this IServiceCollection services)
    {
        // Singletons because the fixtures are immutable and hold no connection or per-request state.
        services.AddSingleton<IDataSourceDescriptor, StubDataSourceDescriptor>();
        services.AddSingleton<IOrganisationProvider, StubOrganisationProvider>();
        services.AddSingleton<IPackagingDataProvider, StubPackagingDataProvider>();
        services.AddSingleton<IComplianceSchemeProvider, StubComplianceSchemeProvider>();

        return services;
    }
}
