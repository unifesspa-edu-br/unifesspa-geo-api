namespace Unifesspa.Geo.IntegrationTests.Registros;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Unifesspa.Geo.Infrastructure.Core.Middleware;
using Unifesspa.Geo.IntegrationTests.TestSupport;

/// <summary>
/// Monta o middleware de registro da requisição com um registro em memória e executa uma requisição simulada.
/// </summary>
internal sealed class RequisicaoComRegistro
{
    private readonly CapturaDeRegistros _captura = new();
    private readonly RequestLoggingMiddleware _middleware;

    public RequisicaoComRegistro(RequestDelegate proximo, IEnumerable<string> prefixosSilenciados)
    {
        RequestLoggingOptions opcoes = new();
        opcoes.PrefixosSilenciados.Clear();
        foreach (string prefixo in prefixosSilenciados)
        {
            opcoes.PrefixosSilenciados.Add(prefixo);
        }

        IOptions<RequestLoggingOptions> configuracao = Options.Create(opcoes);
        _middleware = new RequestLoggingMiddleware(
            proximo,
            _captura.Para<RequestLoggingMiddleware>(),
            new QueryStringMasker(configuracao),
            configuracao);
    }

    public IReadOnlyList<RegistroCapturado> Registros => _captura.Registros;

    /// <summary>Próximo passo do pipeline que responde com o status indicado.</summary>
    public static RequestDelegate ResponderCom(int status) =>
        contexto =>
        {
            contexto.Response.StatusCode = status;
            return Task.CompletedTask;
        };

    public Task ExecutarAsync(string metodo, string caminho, string consulta)
    {
        DefaultHttpContext contexto = new();
        contexto.Request.Method = metodo;
        contexto.Request.Path = caminho;
        contexto.Request.QueryString = new QueryString(consulta);
        return _middleware.InvokeAsync(contexto);
    }
}
