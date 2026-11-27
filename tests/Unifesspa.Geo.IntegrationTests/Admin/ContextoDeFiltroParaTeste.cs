namespace Unifesspa.Geo.IntegrationTests.Admin;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

/// <summary>Monta o contexto de um filtro de recurso e registra se o restante do pedido foi executado.</summary>
internal sealed class ContextoDeFiltroParaTeste
{
    private readonly Exception? _falhaNoRestante;

    public ContextoDeFiltroParaTeste(
        DefaultHttpContext httpContext,
        Exception? falhaNoRestante = null)
    {
        _falhaNoRestante = falhaNoRestante;
        Contexto = new ResourceExecutingContext(
            new ActionContext(httpContext, new RouteData(), new ActionDescriptor()),
            [],
            []);
    }

    public ResourceExecutingContext Contexto { get; }

    public bool RestanteExecutado { get; private set; }

    public ResourceExecutionDelegate Proximo => ExecutarRestanteAsync;

    public ResourceExecutedContext? Executado { get; private set; }

    public int? StatusDaRecusa => (Contexto.Result as ObjectResult)?.StatusCode;

    public static DefaultHttpContext Requisicao(string? tipoDeConteudo, long? tamanho)
    {
        DefaultHttpContext http = new();
        http.Request.ContentType = tipoDeConteudo;
        http.Request.ContentLength = tamanho;
        return http;
    }

    private Task<ResourceExecutedContext> ExecutarRestanteAsync()
    {
        RestanteExecutado = true;
        if (_falhaNoRestante is not null)
        {
            throw _falhaNoRestante;
        }

        Executado = new ResourceExecutedContext(Contexto, Contexto.Filters);
        return Task.FromResult(Executado);
    }
}
