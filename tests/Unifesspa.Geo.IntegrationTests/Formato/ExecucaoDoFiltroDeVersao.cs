namespace Unifesspa.Geo.IntegrationTests.Formato;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

using Unifesspa.Geo.Infrastructure.Core.Formatting;

/// <summary>
/// Executa o filtro de versão de mídia do recurso <c>estado</c> sobre uma requisição montada à mão,
/// sem servidor: primeiro a negociação do <c>Accept</c> e, se aceita, a escrita da resposta.
/// </summary>
internal sealed class ExecucaoDoFiltroDeVersao
{
    private readonly IServiceProvider _servicos;
    private readonly VendorMediaTypeAttribute _filtro;

    public ExecucaoDoFiltroDeVersao(IServiceProvider servicos, string recurso, int[] versoes)
    {
        _servicos = servicos;
        _filtro = new VendorMediaTypeAttribute { Resource = recurso, Versions = versoes };
    }

    /// <summary>Requisição com o valor de Accept informado; <see langword="null"/> deixa o cabeçalho ausente.</summary>
    public DefaultHttpContext NovaRequisicao(string? accept)
    {
        DefaultHttpContext contexto = new() { RequestServices = _servicos };
        if (accept is not null)
        {
            contexto.Request.Headers.Accept = accept;
        }

        return contexto;
    }

    public DesfechoDaNegociacao Negociar(HttpContext contexto, IActionResult? resultadoDaAcao = null)
    {
        ActionContext acao = new(contexto, new RouteData(), new ActionDescriptor());
        ActionExecutingContext antes = new(acao, [], new Dictionary<string, object?>(StringComparer.Ordinal), controller: new object());
        _filtro.OnActionExecuting(antes);
        if (antes.Result is ObjectResult recusa)
        {
            return new DesfechoDaNegociacao(recusa, null, []);
        }

        _filtro.OnResultExecuting(new ResultExecutingContext(acao, [], resultadoDaAcao ?? new OkObjectResult(new object()), new object()));
        return new DesfechoDaNegociacao(null, contexto.Response.ContentType, [.. contexto.Response.Headers.Vary.OfType<string>()]);
    }
}
