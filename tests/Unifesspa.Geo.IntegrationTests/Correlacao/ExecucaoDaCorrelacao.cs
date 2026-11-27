namespace Unifesspa.Geo.IntegrationTests.Correlacao;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;

using Unifesspa.Geo.Infrastructure.Core.Middleware;

/// <summary>Resultado de uma requisição simulada pelo middleware de correlação.</summary>
internal sealed class ExecucaoDaCorrelacao
{
    private ExecucaoDaCorrelacao(string? cabecalhoDaResposta, bool proximoFoiChamado)
    {
        CabecalhoDaResposta = cabecalhoDaResposta;
        ProximoFoiChamado = proximoFoiChamado;
    }

    public string? CabecalhoDaResposta { get; }

    public bool ProximoFoiChamado { get; }

    public static Task<ExecucaoDaCorrelacao> RodarAsync(StringValues enviado, ICorrelationIdWriter escritor) =>
        RodarAsync(enviado, escritor, _ => Task.CompletedTask);

    public static async Task<ExecucaoDaCorrelacao> RodarAsync(
        StringValues enviado,
        ICorrelationIdWriter escritor,
        RequestDelegate proximo)
    {
        CapturaDoInicioDaResposta resposta = new();
        DefaultHttpContext contexto = ContextoCom(enviado, resposta);

        bool chamado = false;
        await new CorrelationIdMiddleware(c =>
        {
            chamado = true;
            return proximo(c);
        }).InvokeAsync(contexto, escritor);
        await resposta.IniciarAsync();

        return new ExecucaoDaCorrelacao(CabecalhoDe(contexto), chamado);
    }

    private static DefaultHttpContext ContextoCom(StringValues enviado, CapturaDoInicioDaResposta resposta)
    {
        DefaultHttpContext contexto = new();
        contexto.Features.Set<IHttpResponseFeature>(resposta);
        if (!StringValues.IsNullOrEmpty(enviado))
        {
            contexto.Request.Headers[CorrelationIdMiddleware.HeaderName] = enviado;
        }

        return contexto;
    }

    private static string? CabecalhoDe(DefaultHttpContext contexto) =>
        contexto.Response.Headers.TryGetValue(CorrelationIdMiddleware.HeaderName, out StringValues valor)
            ? valor.ToString()
            : null;
}
