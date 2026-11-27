namespace Unifesspa.Geo.Infrastructure.Core.OpenApi;

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

/// <summary>
/// Declara no esquema <c>ProblemDetails</c> as propriedades que toda resposta de erro do Geo carrega
/// por <c>ProblemDetails.Extensions</c>: o <c>code</c> e o <c>traceId</c>. O gerador as descreve como
/// propriedades adicionais, e não como membros; sem esta declaração quem gera cliente tipado a partir
/// do contrato não tem campo para tratar o erro.
/// </summary>
public sealed class ProblemDetailsExtensionsDocumentTransformer : IOpenApiDocumentTransformer
{
    /// <summary>Formato de todo código de erro: <c>uniplus.</c> seguido de um ou mais assuntos em minúsculas.</summary>
    public const string PadraoDoCodigo = @"^uniplus(\.[a-z0-9_]+){1,}$";

    private const string NomeDoEsquema = "ProblemDetails";
    private const string DescricaoDoCodigo =
        "Identificador estável do erro. A página do portal que o explica é o campo `type`.";
    private const string DescricaoDoTraceId =
        "Identificador da requisição, para localizar o erro nos registros do serviço.";

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(context);

        if (document.Components?.Schemas?.TryGetValue(NomeDoEsquema, out IOpenApiSchema? esquema) == true
            && esquema is OpenApiSchema problemDetails)
        {
            Declarar(problemDetails);
        }

        return Task.CompletedTask;
    }

    private static void Declarar(OpenApiSchema problemDetails)
    {
        problemDetails.Properties ??= new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
        problemDetails.Properties["code"] = new OpenApiSchema
        {
            Type = JsonSchemaType.String,
            Pattern = PadraoDoCodigo,
            Description = DescricaoDoCodigo,
        };
        problemDetails.Properties["traceId"] = new OpenApiSchema
        {
            Type = JsonSchemaType.String,
            Description = DescricaoDoTraceId,
        };
        problemDetails.Required ??= new HashSet<string>(StringComparer.Ordinal);
        problemDetails.Required.Add("code");
    }
}
