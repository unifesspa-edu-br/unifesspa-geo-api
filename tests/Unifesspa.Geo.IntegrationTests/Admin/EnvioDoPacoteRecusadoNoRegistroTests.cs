namespace Unifesspa.Geo.IntegrationTests.Admin;

using System.Net;

using AwesomeAssertions;

using Unifesspa.Geo.IntegrationTests.Etl;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Recusas que dependem do conteúdo do pacote ou do banco: conferência do zip (422), carga em
/// andamento e versão anterior à última aplicada (409). Em todas, o arquivo é apagado e o conteúdo
/// enviado não aparece na resposta nem nos registros de log.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class EnvioDoPacoteRecusadoNoRegistroTests
{
    private readonly GeoPostgisFixture _fixture;
    private readonly BancoDaCarga _banco;

    public EnvioDoPacoteRecusadoNoRegistroTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _banco = new BancoDaCarga(fixture);
    }

    public static TheoryData<string> ZipsInvalidos => new() { "nao-e-zip", "sem-uma-tabela", "versao-misturada" };

    [Theory(DisplayName = "Pacote inválido retorna 422, apaga o arquivo e não expõe o conteúdo")]
    [MemberData(nameof(ZipsInvalidos))]
    public async Task PacoteInvalido_Retorna422(string caso)
    {
        await _banco.LimparAsync();
        await using HostDoEnvioDoPacote host = new(_fixture.Factory);
        using ByteArrayContent corpo = HostDoEnvioDoPacote.Zip(ZipInvalido(caso));

        using HttpResponseMessage resposta = await host.EnviarAsync(corpo);

        resposta.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        string texto = await resposta.Content.ReadAsStringAsync();
        texto.Should().Contain("uniplus.geo.importacao.pacote_invalido").And.NotContain(ZipsParaOEnvio.MarcadorSigiloso);
        host.NomesDosArquivos().Should().BeEmpty();
        host.Logs.TextoCompleto.Should().NotContain(ZipsParaOEnvio.MarcadorSigiloso);
    }

    [Fact(DisplayName = "Com uma carga em andamento, o envio retorna 409 antes de gravar o corpo")]
    public async Task ComCargaEmAndamento_Retorna409SemGravarOCorpo()
    {
        await _banco.LimparAsync();
        await using HostDoEnvioDoPacote host = new(_fixture.Factory);
        await new ExecucoesSemeadasParaOEnvio(_fixture).EmAndamentoAsync("202610", ZipsParaOEnvio.Valido("202610"));
        using ByteArrayContent corpo = HostDoEnvioDoPacote.Zip(ZipsParaOEnvio.Valido("202611"));

        using HttpResponseMessage resposta = await host.EnviarAsync(corpo);

        resposta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await resposta.Content.ReadAsStringAsync()).Should().Contain("uniplus.geo.importacao.em_andamento");
        host.NomesDosArquivos().Should().BeEmpty();
    }

    [Fact(DisplayName = "Versão anterior à última concluída retorna 409 e apaga o arquivo")]
    public async Task VersaoAnteriorAUltimaConcluida_Retorna409()
    {
        await _banco.LimparAsync();
        await new ExecucoesSemeadasParaOEnvio(_fixture).ConcluidaAsync("202611", ZipsParaOEnvio.Valido("202611"));
        await using HostDoEnvioDoPacote host = new(_fixture.Factory);
        using ByteArrayContent corpo = HostDoEnvioDoPacote.Zip(ZipsParaOEnvio.Valido("202610"));

        using HttpResponseMessage resposta = await host.EnviarAsync(corpo);

        resposta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await resposta.Content.ReadAsStringAsync()).Should().Contain("uniplus.geo.importacao.versao_nao_progressiva");
        host.NomesDosArquivos().Should().BeEmpty();
    }

    private static byte[] ZipInvalido(string caso) => caso switch
    {
        "sem-uma-tabela" => ZipsParaOEnvio.SemUmaTabela(),
        "versao-misturada" => ZipsParaOEnvio.ComVersaoMisturada(),
        _ => ZipsParaOEnvio.NaoEhZip(),
    };
}
