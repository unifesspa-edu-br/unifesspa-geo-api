namespace Unifesspa.Geo.Infrastructure.Core.DependencyInjection;

using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using OpenApi;

/// <summary>
/// Registra o pipeline de transformers do Geo e o documento OpenAPI nomeado
/// (<paramref name="documentName"/>), que o serviço publica com o nome <c>"geo"</c>.
/// Os transformers são reutilizados (<c>TryAddSingleton</c>).
/// </summary>
public static class GeoOpenApiServiceCollectionExtensions
{
    public static IServiceCollection AddGeoOpenApi(
        this IServiceCollection services,
        string documentName,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentName);
        ArgumentNullException.ThrowIfNull(configuration);

        return services
            .AddOpcoesDoOpenApi(configuration)
            .AddTransformersDoOpenApi()
            .AddOpenApi(documentName, ConfigurarDocumento);
    }

    private static IServiceCollection AddOpcoesDoOpenApi(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<GeoOpenApiOptions>()
            .Bind(configuration.GetSection(GeoOpenApiOptions.SectionName))
            .Validate(
                static o => Uri.TryCreate(o.ContactUrl, UriKind.Absolute, out _)
                    && Uri.TryCreate(o.ProductionServerUrl, UriKind.Absolute, out _)
                    && Uri.TryCreate(o.StagingServerUrl, UriKind.Absolute, out _),
                "Geo:OpenApi — ContactUrl/ProductionServerUrl/StagingServerUrl precisam ser URIs absolutas.")
            .ValidateOnStart();

        return services;
    }

    private static IServiceCollection AddTransformersDoOpenApi(this IServiceCollection services)
    {
        services.TryAddSingleton<GeoInfoTransformer>();
        services.TryAddSingleton<GeoOperationTransformer>();
        services.TryAddSingleton<CursorPaginationOperationTransformer>();
        services.TryAddSingleton<PaginationOrphanSchemaDocumentTransformer>();
        services.TryAddSingleton<GeoSchemaTransformer>();
        services.TryAddSingleton<ProblemDetailsExtensionsDocumentTransformer>();

        return services;
    }

    private static void ConfigurarDocumento(OpenApiOptions options)
    {
        options.AddDocumentTransformer<GeoInfoTransformer>();
        options.AddOperationTransformer<GeoOperationTransformer>();
        options.AddOperationTransformer<CursorPaginationOperationTransformer>();
        options.AddDocumentTransformer<PaginationOrphanSchemaDocumentTransformer>();
        options.AddSchemaTransformer<GeoSchemaTransformer>();
        options.AddDocumentTransformer<ProblemDetailsExtensionsDocumentTransformer>();
    }
}
