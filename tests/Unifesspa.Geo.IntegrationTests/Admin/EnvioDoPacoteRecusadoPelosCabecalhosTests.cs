namespace Unifesspa.Geo.IntegrationTests.Admin;

using System.Net;
using System.Net.Http.Headers;

using AwesomeAssertions;

using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// O tipo de conteúdo (415) e o tamanho (413, declarado ou contado durante o fluxo) são conferidos
/// antes de gravar qualquer coisa. As respostas são <c>application/problem+json</c>.
/// </summary>
public sealed class EnvioDoPacoteRecusadoPelosCabecalhosTests : IClassFixture<GeoOpenApiFactory>
{
    private const int LimiteDoTeste = 1024;

    private readonly GeoOpenApiFactory _fabrica;

    public EnvioDoPacoteRecusadoPelosCabecalhosTests(GeoOpenApiFactory fabrica)
    {
        _fabrica = fabrica;
    }

    [Theory(DisplayName = "Tipo de conteúdo diferente de application/zip retorna 415 sem gravar o corpo")]
    [InlineData("application/json")]
    [InlineData("text/plain")]
    [InlineData("application/octet-stream")]
    public async Task TipoDeConteudoDiferenteDeZip_Retorna415(string tipo)
    {
        await using HostDoEnvioDoPacote host = new(_fabrica);
        using ByteArrayContent corpo = new(ZipsParaOEnvio.NaoEhZip());
        corpo.Headers.ContentType = new MediaTypeHeaderValue(tipo);

        using HttpResponseMessage resposta = await host.EnviarAsync(corpo);

        await ConferirProblemaAsync(resposta, HttpStatusCode.UnsupportedMediaType, "tipo_de_conteudo_nao_suportado", host);
    }

    [Fact(DisplayName = "Envio sem tipo de conteúdo retorna 415")]
    public async Task SemTipoDeConteudo_Retorna415()
    {
        await using HostDoEnvioDoPacote host = new(_fabrica);
        using ByteArrayContent corpo = new(ZipsParaOEnvio.NaoEhZip());

        using HttpResponseMessage resposta = await host.EnviarAsync(corpo);

        await ConferirProblemaAsync(resposta, HttpStatusCode.UnsupportedMediaType, "tipo_de_conteudo_nao_suportado", host);
    }

    [Fact(DisplayName = "Content-Length acima do limite retorna 413 sem ler o corpo")]
    public async Task ContentLengthAcimaDoLimite_Retorna413()
    {
        await using HostDoEnvioDoPacote host = new(_fabrica, LimiteDoTeste);
        using ByteArrayContent corpo = HostDoEnvioDoPacote.Zip(ZipsParaOEnvio.AcimaDe(LimiteDoTeste));

        using HttpResponseMessage resposta = await host.EnviarAsync(corpo);

        await ConferirProblemaAsync(resposta, HttpStatusCode.RequestEntityTooLarge, "pacote_acima_do_limite", host);
    }

    [Fact(DisplayName = "Envio em fluxo, sem Content-Length, acima do limite retorna 413 e apaga o arquivo parcial")]
    public async Task FluxoSemContentLengthAcimaDoLimite_Retorna413()
    {
        await using HostDoEnvioDoPacote host = new(_fabrica, LimiteDoTeste);
        using ConteudoEmFluxoSemTamanho corpo = new(ZipsParaOEnvio.AcimaDe(LimiteDoTeste), "application/zip");

        using HttpResponseMessage resposta = await host.EnviarAsync(corpo);

        await ConferirProblemaAsync(resposta, HttpStatusCode.RequestEntityTooLarge, "pacote_acima_do_limite", host);
    }

    private static async Task ConferirProblemaAsync(
        HttpResponseMessage resposta,
        HttpStatusCode status,
        string codigoDoErro,
        HostDoEnvioDoPacote host)
    {
        resposta.StatusCode.Should().Be(status);
        resposta.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await resposta.Content.ReadAsStringAsync()).Should().Contain($"uniplus.geo.importacao.{codigoDoErro}");
        host.NomesDosArquivos().Should().BeEmpty();
    }
}
