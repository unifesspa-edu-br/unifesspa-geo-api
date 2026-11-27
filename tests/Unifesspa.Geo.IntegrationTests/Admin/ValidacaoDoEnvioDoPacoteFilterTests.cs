namespace Unifesspa.Geo.IntegrationTests.Admin;

using AwesomeAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

using Unifesspa.Geo.API.Filters;
using Unifesspa.Geo.Infrastructure.Persistence.Etl;

/// <summary>O filtro recusa pelos cabeçalhos, sem ler o corpo: tipo de conteúdo (415) e tamanho declarado (413).</summary>
public sealed class ValidacaoDoEnvioDoPacoteFilterTests
{
    private const long Limite = 1000;

    private readonly ValidacaoDoEnvioDoPacoteFilter _filtro = new(
        Options.Create(new EtlOpcoes { TamanhoMaximoDoPacote = Limite }),
        new MapeadorDeErrosParaTeste());

    [Theory(DisplayName = "Tipo de conteúdo diferente de application/zip é recusado com 415")]
    [InlineData("application/json")]
    [InlineData("text/plain")]
    [InlineData("multipart/form-data; boundary=x")]
    [InlineData(null)]
    [InlineData("")]
    public async Task TipoDiferenteDeZip_Retorna415(string? tipo)
    {
        ContextoDeFiltroParaTeste contexto = new(ContextoDeFiltroParaTeste.Requisicao(tipo, 10));

        await _filtro.OnResourceExecutionAsync(contexto.Contexto, contexto.Proximo);

        contexto.StatusDaRecusa.Should().Be(StatusCodes.Status415UnsupportedMediaType);
        contexto.RestanteExecutado.Should().BeFalse();
    }

    [Theory(DisplayName = "Tipo application/zip é aceito, com qualquer caixa e com parâmetros")]
    [InlineData("application/zip")]
    [InlineData("Application/ZIP")]
    [InlineData("application/zip; charset=binary")]
    public async Task TipoZip_Segue(string tipo)
    {
        ContextoDeFiltroParaTeste contexto = new(ContextoDeFiltroParaTeste.Requisicao(tipo, 10));

        await _filtro.OnResourceExecutionAsync(contexto.Contexto, contexto.Proximo);

        contexto.RestanteExecutado.Should().BeTrue();
        contexto.Contexto.Result.Should().BeNull();
    }

    [Fact(DisplayName = "Content-Length acima do limite é recusado com 413 sem executar o restante")]
    public async Task ContentLengthAcimaDoLimite_Retorna413()
    {
        ContextoDeFiltroParaTeste contexto = new(ContextoDeFiltroParaTeste.Requisicao("application/zip", Limite + 1));

        await _filtro.OnResourceExecutionAsync(contexto.Contexto, contexto.Proximo);

        contexto.StatusDaRecusa.Should().Be(StatusCodes.Status413PayloadTooLarge);
        contexto.RestanteExecutado.Should().BeFalse();
    }

    [Fact(DisplayName = "Content-Length igual ao limite é aceito")]
    public async Task ContentLengthNoLimite_Segue()
    {
        ContextoDeFiltroParaTeste contexto = new(ContextoDeFiltroParaTeste.Requisicao("application/zip", Limite));

        await _filtro.OnResourceExecutionAsync(contexto.Contexto, contexto.Proximo);

        contexto.RestanteExecutado.Should().BeTrue();
    }

    [Fact(DisplayName = "Sem Content-Length o envio segue e o servidor passa a limitar o corpo ao tamanho máximo")]
    public async Task SemContentLength_SeguELimitaOCorpoDoServidor()
    {
        DefaultHttpContext http = ContextoDeFiltroParaTeste.Requisicao("application/zip", null);
        LimiteDoCorpoParaTeste limite = new(somenteLeitura: false);
        http.Features.Set<IHttpMaxRequestBodySizeFeature>(limite);
        ContextoDeFiltroParaTeste contexto = new(http);

        await _filtro.OnResourceExecutionAsync(contexto.Contexto, contexto.Proximo);

        contexto.RestanteExecutado.Should().BeTrue();
        limite.MaxRequestBodySize.Should().Be(Limite + 1);
    }

    [Fact(DisplayName = "Limite do servidor já travado para leitura é deixado como está")]
    public async Task LimiteSomenteLeitura_NaoEhAlterado()
    {
        DefaultHttpContext http = ContextoDeFiltroParaTeste.Requisicao("application/zip", 10);
        LimiteDoCorpoParaTeste limite = new(somenteLeitura: true);
        http.Features.Set<IHttpMaxRequestBodySizeFeature>(limite);
        ContextoDeFiltroParaTeste contexto = new(http);

        await _filtro.OnResourceExecutionAsync(contexto.Contexto, contexto.Proximo);

        limite.MaxRequestBodySize.Should().Be(LimiteDoCorpoParaTeste.ValorInicial);
    }

    [Fact(DisplayName = "Recusa do servidor por tamanho durante a leitura da ação vira 413 tratado no estágio de exceção")]
    public async Task RecusaDoServidorNaLeitura_Vira413Tratado()
    {
        ExceptionContext contexto = ContextoDaExcecao(new BadHttpRequestException("limite do servidor", StatusCodes.Status413PayloadTooLarge));

        await _filtro.OnExceptionAsync(contexto);

        (contexto.Result as ObjectResult)?.StatusCode.Should().Be(StatusCodes.Status413PayloadTooLarge);
        contexto.ExceptionHandled.Should().BeTrue();
    }

    [Fact(DisplayName = "Outra exceção durante a leitura não é tratada pelo filtro")]
    public async Task OutraExcecaoNaLeitura_NaoEhTratada()
    {
        ExceptionContext contexto = ContextoDaExcecao(new BadHttpRequestException("corpo malformado", StatusCodes.Status400BadRequest));

        await _filtro.OnExceptionAsync(contexto);

        contexto.ExceptionHandled.Should().BeFalse();
        contexto.Result.Should().BeNull();
    }

    private static ExceptionContext ContextoDaExcecao(Exception excecao) =>
        new(new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()), [])
        {
            Exception = excecao,
        };

    private sealed class LimiteDoCorpoParaTeste : IHttpMaxRequestBodySizeFeature
    {
        public const long ValorInicial = 30_000_000;

        public LimiteDoCorpoParaTeste(bool somenteLeitura)
        {
            IsReadOnly = somenteLeitura;
        }

        public bool IsReadOnly { get; }

        public long? MaxRequestBodySize { get; set; } = ValorInicial;
    }
}
