namespace Unifesspa.Geo.IntegrationTests.Registros;

using AwesomeAssertions;

using Microsoft.Extensions.Logging;

/// <summary>
/// Respostas de sucesso em caminho que começa por um prefixo silenciado não são registradas; o prefixo vale só
/// até uma barra e respostas de erro são sempre registradas.
/// </summary>
public sealed class PrefixosSilenciadosNaRequisicaoTests
{
    [Theory(DisplayName = "Caminho igual ao prefixo ou abaixo dele não é registrado em sucesso")]
    [InlineData("/saude", "/saude")]
    [InlineData("/saude", "/saude/")]
    [InlineData("/saude/", "/saude")]
    [InlineData("/saude/", "/saude/")]
    [InlineData("/saude", "/saude/pronta")]
    [InlineData("/saude", "/saude/banco/postgres")]
    [InlineData("/saude", "/SAUDE/Pronta")]
    [InlineData("/saude/", "/saude/pronta/")]
    public async Task CaminhoSilenciadoNaoERegistradoEmSucesso(string prefixo, string caminho)
    {
        RequisicaoComRegistro requisicao = new(RequisicaoComRegistro.ResponderCom(200), [prefixo]);

        await requisicao.ExecutarAsync("GET", caminho, string.Empty);

        requisicao.Registros.Should().BeEmpty();
    }

    [Theory(DisplayName = "Prefixo parecido, mas sem barra de separação, não silencia")]
    [InlineData("/saude", "/saudavel")]
    [InlineData("/saude", "/saude-ui")]
    [InlineData("/saude", "/saudecheck")]
    [InlineData("/saude", "/outra/saude")]
    public async Task PrefixoParecidoNaoSilencia(string prefixo, string caminho)
    {
        RequisicaoComRegistro requisicao = new(RequisicaoComRegistro.ResponderCom(200), [prefixo]);

        await requisicao.ExecutarAsync("GET", caminho, string.Empty);

        requisicao.Registros.Should().ContainSingle().Which.Nivel.Should().Be(LogLevel.Information);
    }

    [Theory(DisplayName = "Erro em caminho silenciado é sempre registrado")]
    [InlineData(404, LogLevel.Warning)]
    [InlineData(503, LogLevel.Error)]
    public async Task ErroEmCaminhoSilenciadoERegistrado(int status, LogLevel nivel)
    {
        RequisicaoComRegistro requisicao = new(RequisicaoComRegistro.ResponderCom(status), ["/saude"]);

        await requisicao.ExecutarAsync("GET", "/saude/pronta", string.Empty);

        requisicao.Registros.Should().ContainSingle().Which.Nivel.Should().Be(nivel);
    }

    [Fact(DisplayName = "Sem prefixos configurados nenhum caminho é silenciado")]
    public async Task SemPrefixosNenhumCaminhoESilenciado()
    {
        RequisicaoComRegistro requisicao = new(RequisicaoComRegistro.ResponderCom(200), []);

        await requisicao.ExecutarAsync("GET", "/health", string.Empty);

        requisicao.Registros.Should().ContainSingle();
    }

    [Fact(DisplayName = "Com vários prefixos, qualquer um deles silencia")]
    public async Task ComVariosPrefixosQualquerUmSilencia()
    {
        RequisicaoComRegistro requisicao = new(RequisicaoComRegistro.ResponderCom(200), ["/health", "/metrics"]);

        await requisicao.ExecutarAsync("GET", "/metrics", string.Empty);

        requisicao.Registros.Should().BeEmpty();
    }
}
