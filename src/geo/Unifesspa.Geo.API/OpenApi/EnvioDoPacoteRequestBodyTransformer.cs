namespace Unifesspa.Geo.API.OpenApi;

using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

using Unifesspa.Geo.API.Controllers;

/// <summary>
/// Declara no contrato o corpo do envio do pacote: o arquivo zip em binário (<c>application/zip</c>).
/// O corpo é lido direto do fluxo da requisição, sem parâmetro de ação, então o ApiExplorer não o descreve.
/// </summary>
internal sealed class EnvioDoPacoteRequestBodyTransformer : IOpenApiOperationTransformer
{
    private const string TipoDeConteudoDoPacote = "application/zip";

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        if (EhOEnvioDoPacote(context))
        {
            operation.RequestBody = new OpenApiRequestBody
            {
                Description = "Pacote dos Correios em formato zip, enviado como o corpo da requisição.",
                Required = true,
                Content = new Dictionary<string, OpenApiMediaType>(StringComparer.Ordinal)
                {
                    [TipoDeConteudoDoPacote] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" },
                    },
                },
            };
        }

        return Task.CompletedTask;
    }

    private static bool EhOEnvioDoPacote(OpenApiOperationTransformerContext context) =>
        context.Description.ActionDescriptor is ControllerActionDescriptor descriptor
        && descriptor.ControllerTypeInfo.AsType() == typeof(GeoImportacoesController)
        && string.Equals(descriptor.ActionName, nameof(GeoImportacoesController.Enviar), StringComparison.Ordinal);
}
