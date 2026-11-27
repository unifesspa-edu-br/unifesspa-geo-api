namespace Unifesspa.Geo.Infrastructure.Core.Middleware;

using Errors;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// A resposta de erro da requisição que não chegou a uma operação da API: 404 com <c>uniplus.requisicao.caminho_inexistente</c>
/// (a rota que não existe e o identificador de rota fora do formato que a operação aceita, que o roteamento descarta do mesmo
/// modo) e 405 com <c>uniplus.requisicao.metodo_nao_aceito</c> (a rota existe, mas não aceita o método).
/// </summary>
/// <remarks>
/// Só escreve quando nenhuma operação foi selecionada: sem endpoint (404) ou com o endpoint que o roteamento põe no 405, que
/// não é um endpoint de rota. O 404 de registro inexistente, em que a operação foi selecionada, já sai com o código do
/// assunto e não passa por aqui. A resposta não é limpa antes da escrita, para preservar o cabeçalho <c>Allow</c> que o
/// roteamento grava no 405. Nem o caminho nem o método pedidos vão à resposta.
/// </remarks>
public static class RespostaDaRequisicaoSemOperacao
{
    private const string DetalheDoCaminhoInexistente =
        "Nenhuma operação da API atende a este caminho. Confira o caminho e o formato dos identificadores na referência da API.";

    private const string DetalheDoMetodoNaoAceito =
        "O caminho existe, mas não aceita o método usado. O cabeçalho Allow da resposta informa os métodos aceitos.";

    public static IApplicationBuilder UseRespostaDaRequisicaoSemOperacao(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseStatusCodePages(static ctx => EscreverSeNenhumaOperacaoAtende(ctx.HttpContext));
    }

    private static Task EscreverSeNenhumaOperacaoAtende(HttpContext context)
    {
        if (context.GetEndpoint() is RouteEndpoint)
        {
            return Task.CompletedTask;
        }

        return RespostaDoStatus(context.Response.StatusCode) is var (definicao, detalhe)
            ? ProblemaNaResposta.EscreverAsync(context, context.RequestServices.GetRequiredService<ProblemTypeUriFactory>(), definicao, detalhe)
            : Task.CompletedTask;
    }

    private static (DomainErrorMapping Definicao, string Detalhe)? RespostaDoStatus(int status) =>
        status switch
        {
            StatusCodes.Status404NotFound => (ErrosDaFronteiraHttp.CaminhoInexistente, DetalheDoCaminhoInexistente),
            StatusCodes.Status405MethodNotAllowed => (ErrosDaFronteiraHttp.MetodoNaoAceito, DetalheDoMetodoNaoAceito),
            _ => null,
        };
}
