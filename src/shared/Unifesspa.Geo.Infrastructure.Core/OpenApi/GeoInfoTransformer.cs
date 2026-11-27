namespace Unifesspa.Geo.Infrastructure.Core.OpenApi;

using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

/// <summary>
/// Document transformer que injeta metadata institucional no documento OpenAPI
/// do Geo. Aplica info
/// (title, description, contact, license em pt-BR), servers por ambiente, e
/// versão de contrato alinhada com ADR-0022 (Contrato REST canônico V1).
/// </summary>
public sealed class GeoInfoTransformer : IOpenApiDocumentTransformer
{
    public const string BearerSecuritySchemeName = "Bearer";

    private readonly GeoOpenApiOptions _options;

    public GeoInfoTransformer(IOptions<GeoOpenApiOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(context);

        document.Info = InformacoesDoModulo(context.DocumentName);
        document.Servers = ServidoresPublicados();
        RegistrarEsquemaBearer(document);

        return Task.CompletedTask;
    }

    private OpenApiInfo InformacoesDoModulo(string nomeDoDocumento) => new()
    {
        Title = TituloDoModulo(nomeDoDocumento),
        Version = _options.ContractVersion,
        Description = "API REST do Sistema Unificado Unifesspa (Uni+). Contrato canônico V1 — "
            + "ProblemDetails RFC 9457, paginação cursor opaco, vendor MIME versioning. "
            + "Toda string user-facing em pt-BR (ADR-0022).",
        Contact = ContatoInstitucional(),
        License = LicencaDoContrato(),
    };

    private static string TituloDoModulo(string nomeDoDocumento) =>
        string.Equals(nomeDoDocumento, "geo", StringComparison.OrdinalIgnoreCase)
            ? "Uni+ — Módulo Geo"
            : $"UNIFESSPA — {nomeDoDocumento}";

    private OpenApiContact ContatoInstitucional() => new()
    {
        Name = _options.ContactName,
        Email = _options.ContactEmail,
        Url = new Uri(_options.ContactUrl),
    };

    private static OpenApiLicense LicencaDoContrato() => new()
    {
        Name = "MIT",
        Url = new Uri("https://opensource.org/licenses/MIT"),
    };

    private IList<OpenApiServer> ServidoresPublicados() =>
    [
        new OpenApiServer { Url = _options.ProductionServerUrl, Description = "Produção" },
        new OpenApiServer { Url = _options.StagingServerUrl, Description = "Homologação" },
    ];

    private static void RegistrarEsquemaBearer(OpenApiDocument document)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??=
            new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);
        document.Components.SecuritySchemes[BearerSecuritySchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Token JWT emitido pelo issuer/realm OIDC configurado para a Geo API.",
        };
    }
}
