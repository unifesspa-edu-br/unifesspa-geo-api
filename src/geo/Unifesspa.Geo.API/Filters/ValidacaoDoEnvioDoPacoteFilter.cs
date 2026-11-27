namespace Unifesspa.Geo.API.Filters;

using System.Diagnostics.CodeAnalysis;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

using Unifesspa.Geo.Domain.Errors;
using Unifesspa.Geo.Infrastructure.Core.Errors;
using Unifesspa.Geo.Infrastructure.Persistence.Etl;
using Unifesspa.Geo.Kernel.Results;

/// <summary>
/// Recusa o envio do pacote pelos cabeçalhos, antes de ler o corpo: tipo de conteúdo diferente de
/// <c>application/zip</c> (415) e tamanho declarado acima do limite configurado (413). Roda depois
/// da autorização; por isso não usa <c>[Consumes]</c>, que seria avaliado no roteamento, antes dela.
/// Também limita o corpo lido pelo servidor ao tamanho máximo e, se o servidor recusar o corpo por
/// excesso de tamanho durante a leitura da ação (envio sem tamanho declarado), responde 413 no
/// estágio de exceção, que ainda executa o resultado da ação.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instanciada via IServiceProvider pelo [ServiceFilter] do controller.")]
internal sealed class ValidacaoDoEnvioDoPacoteFilter : IAsyncResourceFilter, IAsyncExceptionFilter
{
    private const string TipoDeConteudoDoPacote = "application/zip";

    private readonly IOptions<EtlOpcoes> _opcoes;
    private readonly IDomainErrorMapper _mapper;

    public ValidacaoDoEnvioDoPacoteFilter(IOptions<EtlOpcoes> opcoes, IDomainErrorMapper mapper)
    {
        ArgumentNullException.ThrowIfNull(opcoes);
        ArgumentNullException.ThrowIfNull(mapper);
        _opcoes = opcoes;
        _mapper = mapper;
    }

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        DomainError? recusa = Recusar(context.HttpContext.Request);
        if (recusa is not null)
        {
            context.Result = Falha(recusa);
            return;
        }

        LimitarCorpoDoServidor(context.HttpContext);
        await next().ConfigureAwait(false);
    }

    public Task OnExceptionAsync(ExceptionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Exception is BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge })
        {
            context.Result = Falha(ErrosDoPacoteDne.PacoteAcimaDoLimite(_opcoes.Value.TamanhoMaximoDoPacote));
            context.ExceptionHandled = true;
        }

        return Task.CompletedTask;
    }

    private IActionResult Falha(DomainError erro) => Result.Failure(erro).ToActionResult(_mapper);

    private static bool EhZip(string? tipoDeConteudo) =>
        MediaTypeHeaderValue.TryParse(tipoDeConteudo, out MediaTypeHeaderValue? tipo)
        && tipo.MediaType.Equals(TipoDeConteudoDoPacote, StringComparison.OrdinalIgnoreCase);

    private DomainError? Recusar(HttpRequest request)
    {
        if (!EhZip(request.ContentType))
        {
            return ErrosDoPacoteDne.TipoDeConteudoNaoSuportado();
        }

        long limite = _opcoes.Value.TamanhoMaximoDoPacote;
        return request.ContentLength > limite ? ErrosDoPacoteDne.PacoteAcimaDoLimite(limite) : null;
    }

    private void LimitarCorpoDoServidor(HttpContext httpContext)
    {
        IHttpMaxRequestBodySizeFeature? limite = httpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (limite is { IsReadOnly: false })
        {
            limite.MaxRequestBodySize = _opcoes.Value.TamanhoMaximoDoPacote + 1;
        }
    }
}
