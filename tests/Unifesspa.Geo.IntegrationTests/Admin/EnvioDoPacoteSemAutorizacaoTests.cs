namespace Unifesspa.Geo.IntegrationTests.Admin;

using System.Net;

using AwesomeAssertions;

using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Sem login (401) ou sem o papel administrativo (403), o envio é recusado antes de qualquer leitura
/// do corpo: nenhum arquivo é gravado e o conteúdo enviado não aparece na resposta nem nos registros de log.
/// </summary>
public sealed class EnvioDoPacoteSemAutorizacaoTests : IClassFixture<GeoOpenApiFactory>
{
    private readonly GeoOpenApiFactory _fabrica;

    public EnvioDoPacoteSemAutorizacaoTests(GeoOpenApiFactory fabrica)
    {
        _fabrica = fabrica;
    }

    [Fact(DisplayName = "Envio sem autenticação retorna 401 sem ler o corpo")]
    public async Task SemAutenticacao_Retorna401SemLerOCorpo() =>
        await ConferirRecusaSemLerOCorpoAsync(PerfilDoEnvio.Anonimo, HttpStatusCode.Unauthorized);

    [Fact(DisplayName = "Envio autenticado sem o papel plataforma-admin retorna 403 sem ler o corpo")]
    public async Task SemPapelAdministrativo_Retorna403SemLerOCorpo() =>
        await ConferirRecusaSemLerOCorpoAsync(PerfilDoEnvio.SemPapelAdministrativo, HttpStatusCode.Forbidden);

    private async Task ConferirRecusaSemLerOCorpoAsync(PerfilDoEnvio perfil, HttpStatusCode esperado)
    {
        await using HostDoEnvioDoPacote host = new(_fabrica);
        using ByteArrayContent corpo = HostDoEnvioDoPacote.Zip(ZipsParaOEnvio.NaoEhZip());

        using HttpResponseMessage resposta = await host.EnviarAsync(corpo, perfil);

        resposta.StatusCode.Should().Be(esperado);
        host.NomesDosArquivos().Should().BeEmpty("o corpo não foi lido, então nenhum arquivo foi gravado");
        (await resposta.Content.ReadAsStringAsync()).Should().NotContain(ZipsParaOEnvio.MarcadorSigiloso);
        host.Logs.TextoCompleto.Should().NotContain(ZipsParaOEnvio.MarcadorSigiloso);
    }
}
