namespace Unifesspa.Geo.Infrastructure.Core.OpenApi;

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

/// <summary>
/// Schema transformer que completa o tipo das propriedades enumeradas.
/// <para>
/// O <c>code</c> de ProblemDetails NÃO é coberto aqui: o campo vive em
/// <c>ProblemDetails.Extensions["code"]</c> (<c>[JsonExtensionData]</c>) e
/// não recebe um <c>JsonPropertyInfo</c> nomeado — o pattern
/// <c>^[a-z]+(\.[a-z_]+)+$</c> da taxonomia (ADR-0023) é validado a partir
/// do spec gerado pela rule Spectral <c>uniplus-error-code-format</c>.
/// </para>
/// </summary>
public sealed class GeoSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(context);

        InferirTipoDoEnum(schema);

        return Task.CompletedTask;
    }

    // Enum-as-string sem `type` explícito: o ASP.NET Core OpenAPI generator
    // emite `enum: [...]` quando JsonStringEnumConverter está registrado,
    // mas não popula `type: "string"`. Sem o type, geradores de cliente
    // (openapi-typescript, openapi-generator, NSwag) podem tratar a schema
    // como `any`, perdendo type safety. Inferimos string sempre que houver
    // enum sem type — invariante OpenAPI 3.1.
    private static void InferirTipoDoEnum(OpenApiSchema schema)
    {
        if (schema.Enum is { Count: > 0 } && !schema.Type.HasValue)
        {
            schema.Type = JsonSchemaType.String;
        }
    }
}
