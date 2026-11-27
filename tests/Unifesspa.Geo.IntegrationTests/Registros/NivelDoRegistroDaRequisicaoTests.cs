namespace Unifesspa.Geo.IntegrationTests.Registros;

using AwesomeAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

using Unifesspa.Geo.Infrastructure.Core.Middleware;
using Unifesspa.Geo.IntegrationTests.TestSupport;

/// <summary>
/// O nível do registro da requisição segue o resultado: sucesso é Information (salvo prefixo silenciado),
/// erro do cliente é Warning, erro do servidor ou exceção é Error; a exceção é relançada.
/// </summary>
public sealed class NivelDoRegistroDaRequisicaoTests
{
    private const string Modelo = "HTTP {Method} {Path}{Query} respondeu {StatusCode} em {ElapsedMs}ms";

    private static readonly string[] SilenciadosPorPadrao = ["/health", "/metrics"];

    [Theory(DisplayName = "Resposta 2xx e 3xx é registrada como Information")]
    [InlineData(200)]
    [InlineData(204)]
    [InlineData(302)]
    public async Task RespostaDeSucessoEInformation(int status)
    {
        RequisicaoComRegistro requisicao = new(RequisicaoComRegistro.ResponderCom(status), SilenciadosPorPadrao);

        await requisicao.ExecutarAsync("GET", "/cidades", "?pagina=1");

        requisicao.Registros.Should().ContainSingle().Which.Nivel.Should().Be(LogLevel.Information);
    }

    [Theory(DisplayName = "Resposta 4xx é registrada como Warning")]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(499)]
    public async Task RespostaDeErroDoClienteEWarning(int status)
    {
        RequisicaoComRegistro requisicao = new(RequisicaoComRegistro.ResponderCom(status), SilenciadosPorPadrao);

        await requisicao.ExecutarAsync("GET", "/cidades", string.Empty);

        requisicao.Registros.Should().ContainSingle().Which.Nivel.Should().Be(LogLevel.Warning);
    }

    [Theory(DisplayName = "Resposta 5xx sem exceção é registrada como Error")]
    [InlineData(500)]
    [InlineData(503)]
    public async Task RespostaDeErroDoServidorEError(int status)
    {
        RequisicaoComRegistro requisicao = new(RequisicaoComRegistro.ResponderCom(status), SilenciadosPorPadrao);

        await requisicao.ExecutarAsync("GET", "/cidades", string.Empty);

        RegistroCapturado registro = requisicao.Registros.Should().ContainSingle().Subject;
        registro.Nivel.Should().Be(LogLevel.Error);
        registro.Excecao.Should().BeNull();
    }

    [Fact(DisplayName = "Exceção do pipeline é registrada como Error com a exceção e relançada")]
    public async Task ExcecaoEErrorERelancada()
    {
        InvalidOperationException falha = new("falha de teste");
        RequisicaoComRegistro requisicao = new(_ => throw falha, SilenciadosPorPadrao);

        Func<Task> execucao = () => requisicao.ExecutarAsync("POST", "/cidades", string.Empty);

        (await execucao.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(falha);
        RegistroCapturado registro = requisicao.Registros.Should().ContainSingle().Subject;
        registro.Nivel.Should().Be(LogLevel.Error);
        registro.Excecao.Should().BeSameAs(falha);
    }

    [Fact(DisplayName = "Exceção registra o status que a resposta já tinha, sem trocá-lo")]
    public async Task ExcecaoRegistraOStatusDaRespostaNoMomentoDaFalha()
    {
        InvalidOperationException falha = new("falha de teste");
        RequisicaoComRegistro requisicao = new(ContextoComStatus(503, falha), SilenciadosPorPadrao);

        Func<Task> execucao = () => requisicao.ExecutarAsync("GET", "/cidades", string.Empty);

        (await execucao.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(falha);
        requisicao.Registros.Should().ContainSingle().Which.Mensagem.Should().Contain("respondeu 503 em");
    }

    [Fact(DisplayName = "Exceção sem status definido é registrada com o status padrão 200")]
    public async Task ExcecaoSemStatusDefinidoUsaOStatusPadrao()
    {
        RequisicaoComRegistro requisicao = new(_ => throw new InvalidOperationException("falha"), SilenciadosPorPadrao);

        Func<Task> execucao = () => requisicao.ExecutarAsync("GET", "/cidades", string.Empty);

        await execucao.Should().ThrowAsync<InvalidOperationException>();
        requisicao.Registros.Should().ContainSingle().Which.Mensagem.Should().Contain("respondeu 200 em");
    }

    private static RequestDelegate ContextoComStatus(int status, Exception falha) =>
        contexto =>
        {
            contexto.Response.StatusCode = status;
            throw falha;
        };

    [Fact(DisplayName = "Categoria, modelo e texto da mensagem seguem o formato publicado")]
    public async Task CategoriaModeloETextoSeguemOFormatoPublicado()
    {
        RequisicaoComRegistro requisicao = new(RequisicaoComRegistro.ResponderCom(200), SilenciadosPorPadrao);

        await requisicao.ExecutarAsync("GET", "/cidades", "?cpf=1&pagina=2");

        RegistroCapturado registro = requisicao.Registros.Should().ContainSingle().Subject;
        registro.Categoria.Should().Be(typeof(RequestLoggingMiddleware).FullName);
        registro.Modelo.Should().Be(Modelo);
        registro.Mensagem.Should().MatchRegex(@"^HTTP GET /cidades\?cpf=\*\*\*&pagina=2 respondeu 200 em [\d.,]+ms$");
    }

    [Theory(DisplayName = "Warning e Error usam o mesmo modelo de mensagem do Information")]
    [InlineData(404)]
    [InlineData(500)]
    public async Task ErrosUsamOMesmoModelo(int status)
    {
        RequisicaoComRegistro requisicao = new(RequisicaoComRegistro.ResponderCom(status), SilenciadosPorPadrao);

        await requisicao.ExecutarAsync("PUT", "/cidades", string.Empty);

        requisicao.Registros.Should().ContainSingle().Which.Modelo.Should().Be(Modelo);
    }

    [Fact(DisplayName = "Caminho vazio é registrado como barra")]
    public async Task CaminhoVazioERegistradoComoBarra()
    {
        RequisicaoComRegistro requisicao = new(RequisicaoComRegistro.ResponderCom(200), SilenciadosPorPadrao);

        await requisicao.ExecutarAsync("GET", string.Empty, string.Empty);

        requisicao.Registros.Should().ContainSingle().Which.Mensagem.Should().StartWith("HTTP GET / respondeu 200");
    }
}
