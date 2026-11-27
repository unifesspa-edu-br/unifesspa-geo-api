namespace Unifesspa.Geo.IntegrationTests.Admin;

using System.Net;
using System.Text.Json;

using AwesomeAssertions;

using Unifesspa.Geo.IntegrationTests.Etl;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Envio aceito: 202 com o endereço da execução e o arquivo guardado com o identificador dela
/// (permissão 0600); repetição do pacote já publicado devolve 200 sem guardar arquivo.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class EnvioDoPacoteAceitoTests
{
    private readonly GeoPostgisFixture _fixture;
    private readonly BancoDaCarga _banco;

    public EnvioDoPacoteAceitoTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _banco = new BancoDaCarga(fixture);
    }

    [Fact(DisplayName = "Pacote novo retorna 202 com Location, resumo e tamanho, e a execução fica em andamento")]
    public async Task PacoteNovo_Retorna202ComLocation()
    {
        await _banco.LimparAsync();
        byte[] zip = ZipsParaOEnvio.Valido("202610");
        await using HostDoEnvioDoPacote host = new(_fixture.Factory);
        using ByteArrayContent corpo = HostDoEnvioDoPacote.Zip(zip);

        using HttpResponseMessage resposta = await host.EnviarAsync(corpo);

        resposta.StatusCode.Should().Be(HttpStatusCode.Accepted);
        using JsonDocument json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        Guid id = json.RootElement.GetProperty("id").GetGuid();
        resposta.Headers.Location!.ToString().Should().Be($"{HostDoEnvioDoPacote.Rota}/{id}");
        json.RootElement.GetProperty("status").GetString().Should().Be("EmAndamento");
        json.RootElement.GetProperty("versaoDataset").GetString().Should().Be("202610");
        ConferirPacote(json.RootElement.GetProperty("pacote"), zip);
    }

    [Fact(DisplayName = "O arquivo aceito fica com o identificador da execução e permissão 0600")]
    public async Task ArquivoAceito_TemONomeDaExecucaoEPermissao0600()
    {
        await _banco.LimparAsync();
        await using HostDoEnvioDoPacote host = new(_fixture.Factory);
        using ByteArrayContent corpo = HostDoEnvioDoPacote.Zip(ZipsParaOEnvio.Valido("202610"));

        using HttpResponseMessage resposta = await host.EnviarAsync(corpo);

        using JsonDocument json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        string id = json.RootElement.GetProperty("id").GetGuid().ToString("N");
        host.NomesDosArquivos().Should().Equal(id);
        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(Path.Combine(host.Diretorio, id)).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact(DisplayName = "O Location do envio aceito leva ao acompanhamento da execução")]
    public async Task Location_LevaAoAcompanhamentoDaExecucao()
    {
        await _banco.LimparAsync();
        await using HostDoEnvioDoPacote host = new(_fixture.Factory);
        using ByteArrayContent corpo = HostDoEnvioDoPacote.Zip(ZipsParaOEnvio.Valido("202610"));
        using HttpResponseMessage envio = await host.EnviarAsync(corpo);

        using HttpResponseMessage acompanhamento = await host.ObterAsync(envio.Headers.Location!);

        acompanhamento.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact(DisplayName = "Mesmo pacote da última carga concluída retorna 200 com a execução vigente e apaga o arquivo")]
    public async Task MesmoPacoteDaUltimaConcluida_Retorna200ComLocation()
    {
        await _banco.LimparAsync();
        byte[] zip = ZipsParaOEnvio.Valido("202610");
        Guid vigente = await new ExecucoesSemeadasParaOEnvio(_fixture).ConcluidaAsync("202610", zip);
        await using HostDoEnvioDoPacote host = new(_fixture.Factory);
        using ByteArrayContent corpo = HostDoEnvioDoPacote.Zip(zip);

        using HttpResponseMessage resposta = await host.EnviarAsync(corpo);

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        resposta.Headers.Location!.ToString().Should().Be($"{HostDoEnvioDoPacote.Rota}/{vigente}");
        host.NomesDosArquivos().Should().BeEmpty();
    }

    private static void ConferirPacote(JsonElement pacote, byte[] zip)
    {
        pacote.GetProperty("sha256").GetString().Should().Be(ExecucoesSemeadasParaOEnvio.ResumoDe(zip));
        pacote.GetProperty("tamanhoEmBytes").GetInt64().Should().Be(zip.Length);
        pacote.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("sha256", "tamanhoEmBytes");
    }
}
