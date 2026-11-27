namespace Unifesspa.Geo.Infrastructure.Core.Middleware;

using Errors;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// A resposta 404 do caminho que nenhuma operação da API atende (<c>uniplus.requisicao.caminho_inexistente</c>): a rota que
/// não existe e o identificador de rota fora do formato que a operação aceita, que o roteamento descarta do mesmo modo.
/// </summary>
/// <remarks>
/// Só escreve quando nenhuma operação casou. O 404 de registro inexistente, em que a operação casou, já sai com o código do
/// assunto e não passa por aqui. O caminho pedido não vai à resposta.
/// </remarks>
public static class RespostaDaRequisicaoSemOperacao
{
    private const string DetalheDoCaminhoInexistente =
        "Nenhuma operação da API atende a este caminho. Confira o caminho e o formato dos identificadores na referência da API.";

    public static IApplicationBuilder UseRespostaDaRequisicaoSemOperacao(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseStatusCodePages(static ctx => EscreverSeNenhumaOperacaoAtende(ctx.HttpContext));
    }

    private static Task EscreverSeNenhumaOperacaoAtende(HttpContext context) =>
        context.Response.StatusCode == StatusCodes.Status404NotFound && context.GetEndpoint() is null
            ? ProblemaNaResposta.EscreverAsync(
                context,
                context.RequestServices.GetRequiredService<ProblemTypeUriFactory>(),
                ErrosDaFronteiraHttp.CaminhoInexistente,
                DetalheDoCaminhoInexistente)
            : Task.CompletedTask;
}
