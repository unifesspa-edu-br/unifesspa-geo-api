namespace Unifesspa.Geo.Infrastructure.Core.DependencyInjection;

using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using OpenApi;

/// <summary>
/// Registra o pipeline de transformers Uni+ + um documento OpenAPI nomeado
/// (<paramref name="documentName"/>). Cada módulo chama em seu Program.cs com
/// seu próprio nome (ex.: <c>"selecao"</c>, <c>"ingresso"</c>); transformers
/// são reutilizados (<c>TryAddSingleton</c>).
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

        services
            .AddOptions<GeoOpenApiOptions>()
            .Bind(configuration.GetSection(GeoOpenApiOptions.SectionName))
            .Validate(
                static o => Uri.TryCreate(o.ContactUrl, UriKind.Absolute, out _)
                    && Uri.TryCreate(o.ProductionServerUrl, UriKind.Absolute, out _)
                    && Uri.TryCreate(o.StagingServerUrl, UriKind.Absolute, out _),
                "Geo:OpenApi — ContactUrl/ProductionServerUrl/StagingServerUrl precisam ser URIs absolutas.")
            .ValidateOnStart();

        services.TryAddSingleton<GeoInfoTransformer>();
        services.TryAddSingleton<GeoOperationTransformer>();
        services.TryAddSingleton<CursorPaginationOperationTransformer>();
        services.TryAddSingleton<PaginationOrphanSchemaDocumentTransformer>();
        services.TryAddSingleton<GeoSchemaTransformer>();

        services.AddOpenApi(documentName, options =>
        {
            options.AddDocumentTransformer<GeoInfoTransformer>();
            options.AddOperationTransformer<GeoOperationTransformer>();
            options.AddOperationTransformer<CursorPaginationOperationTransformer>();
            options.AddDocumentTransformer<PaginationOrphanSchemaDocumentTransformer>();
            options.AddSchemaTransformer<GeoSchemaTransformer>();
        });

        return services;
    }
}
