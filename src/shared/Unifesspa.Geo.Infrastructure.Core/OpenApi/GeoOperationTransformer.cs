namespace Unifesspa.Geo.Infrastructure.Core.OpenApi;

using System.Text.Json.Nodes;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

/// <summary>
/// Operation transformer que injeta metadata institucional por operação:
/// <list type="bullet">
///   <item><description>Content type de respostas 4xx/5xx coagido para <c>application/problem+json</c> — espelha o que o middleware de fato emite via <c>result.ToActionResult(mapper)</c> (ADR-0023, RFC 9457). Sem isso o spec declararia <c>application/json</c> (default do MVC) e clientes gerados rejeitariam o response real.</description></item>
/// </list>
/// </summary>
public sealed class GeoOperationTransformer : IOpenApiOperationTransformer
{
    private const string ProblemJsonMediaType = "application/problem+json";
    private const string UnauthorizedStatus = "401";
    private const string ForbiddenStatus = "403";
    private const string ProblemDetailsSchemaName = "ProblemDetails";

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        CoerceErrorResponsesToProblemJson(operation);

        IList<object> metadata = context.Description.ActionDescriptor.EndpointMetadata;

        ApplyAuthorizationMetadata(operation, context, metadata);

        return Task.CompletedTask;
    }

    private static void ApplyAuthorizationMetadata(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        IList<object> metadata)
    {
        if (metadata.OfType<IAllowAnonymous>().Any())
            return;

        IAuthorizeData[] authorizeData = [.. metadata.OfType<IAuthorizeData>()];
        if (authorizeData.Length == 0)
            return;

        ExigirBearer(operation, context);
        AddProblemResponseIfMissing(operation, context, UnauthorizedStatus, "Unauthorized");

        if (RestringeOAcesso(authorizeData))
            AddProblemResponseIfMissing(operation, context, ForbiddenStatus, "Forbidden");
    }

    private static bool RestringeOAcesso(IAuthorizeData[] authorizeData) =>
        authorizeData.Any(static data =>
            !string.IsNullOrWhiteSpace(data.Roles) ||
            !string.IsNullOrWhiteSpace(data.Policy));

    private static void ExigirBearer(OpenApiOperation operation, OpenApiOperationTransformerContext context)
    {
        operation.Security ??= [];
        if (operation.Security.Any(ContainsBearerRequirement))
            return;

        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [
                new OpenApiSecuritySchemeReference(
                    GeoInfoTransformer.BearerSecuritySchemeName,
                    context.Document,
                    externalResource: null)
            ] = [],
        });
    }

    private static bool ContainsBearerRequirement(OpenApiSecurityRequirement requirement) =>
        requirement.Keys.Any(static scheme =>
            string.Equals(
                scheme.Reference?.Id,
                GeoInfoTransformer.BearerSecuritySchemeName,
                StringComparison.Ordinal));

    private static void AddProblemResponseIfMissing(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        string statusCode,
        string description)
    {
        operation.Responses ??= new OpenApiResponses();
        if (!operation.Responses.ContainsKey(statusCode))
            operation.Responses[statusCode] = RespostaDeProblema(description, context.Document);
    }

    private static OpenApiResponse RespostaDeProblema(string descricao, OpenApiDocument? documento) => new()
    {
        Description = descricao,
        Content = new Dictionary<string, OpenApiMediaType>(StringComparer.Ordinal)
        {
            [ProblemJsonMediaType] = new()
            {
                Schema = new OpenApiSchemaReference(
                    ProblemDetailsSchemaName,
                    documento,
                    externalResource: null),
            },
        },
    };

    private static void CoerceErrorResponsesToProblemJson(OpenApiOperation operation)
    {
        if (operation.Responses is null)
            return;

        foreach (KeyValuePair<string, IOpenApiResponse> kvp in operation.Responses)
        {
            if (IsErrorStatusKey(kvp.Key) && kvp.Value is OpenApiResponse response)
                CoagirParaProblemJson(response);
        }
    }

    private static void CoagirParaProblemJson(OpenApiResponse response)
    {
        if (ConteudoAindaNaoProblemJson(response) is not { } conteudo)
            return;

        // Espelha o conteúdo declarado (ex.: "application/json" com schema
        // ProblemDetails) para a media type RFC 9457. Mantém o schema
        // declarado e descarta os outros media types — endpoints de erro
        // só falam problem+json no contrato canônico.
        response.Content = new Dictionary<string, OpenApiMediaType>(StringComparer.Ordinal)
        {
            [ProblemJsonMediaType] = OrigemDoConteudo(conteudo),
        };
    }

    // Sem content declarado (raro hoje — JwtBearerProblemDetailsEvents
    // já emite problem+json em 401/403, e endpoints expressam isso via
    // .ProducesProblem). Quando ainda assim acontecer, não inventamos
    // body: escrever `application/problem+json` sobre uma resposta que
    // o runtime emite vazia geraria contrato falso e clientes
    // gerados rejeitariam o 401 real.
    private static IDictionary<string, OpenApiMediaType>? ConteudoAindaNaoProblemJson(OpenApiResponse response) =>
        response.Content is { Count: > 0 } conteudo && !conteudo.ContainsKey(ProblemJsonMediaType)
            ? conteudo
            : null;

    // Prefere application/json explicitamente; sem ele, cai para o primeiro média type
    // (Dictionary não garante ordem de enumeração).
    private static OpenApiMediaType OrigemDoConteudo(IDictionary<string, OpenApiMediaType> conteudo) =>
        conteudo.TryGetValue("application/json", out OpenApiMediaType? jsonMediaType)
            ? jsonMediaType
            : conteudo.Values.First();

    private static bool IsErrorStatusKey(string key)
    {
        if (key.Length != 3)
            return false;

        char first = key[0];
        return first == '4' || first == '5';
    }
}
