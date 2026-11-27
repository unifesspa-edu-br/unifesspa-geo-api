namespace Unifesspa.Geo.IntegrationTests.Etl;

using AwesomeAssertions;

using Npgsql;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Uma falha durante a carga nunca leva o conteúdo da exceção (valores da fonte) para os logs
/// nem para a mensagem da execução; só o tipo da falha e metadados seguros são registrados.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class LogsSanitizadosDaCargaTests
{
    private const string Marcador = "MARCADOR-FICTICIO-4f9d2";

    private readonly GeoPostgisFixture _fixture;
    private readonly BancoDaCarga _banco;

    public LogsSanitizadosDaCargaTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _banco = new BancoDaCarga(fixture);
    }

    [Fact(DisplayName = "Exceção com marcador na leitura da fonte não vaza o marcador para os logs nem para a mensagem da execução")]
    public async Task ExcecaoComMarcador_NaoVazaParaLogsNemMensagem()
    {
        InvalidOperationException falha = new($"valor malformado: {Marcador}", new FormatException(Marcador));

        (RegistroDeLogs logs, GeoImportacaoExecucao execucao) = await ExecutarComFalhaAsync(falha);

        ConferirSemMarcador(logs, execucao);
        logs.TextoCompleto.Should().Contain("Tipo=System.InvalidOperationException; TiposInternos=System.FormatException");
    }

    [Fact(DisplayName = "Falha do banco com marcador no texto e no detalhe não vaza o marcador, mas registra o código do erro")]
    public async Task FalhaDoBancoComMarcador_NaoVazaMasRegistraCodigo()
    {
        PostgresException falha = new(
            $"valor inválido {Marcador}", "ERROR", "ERROR", "22P02", detail: $"Linha com {Marcador}");

        (RegistroDeLogs logs, GeoImportacaoExecucao execucao) = await ExecutarComFalhaAsync(falha);

        ConferirSemMarcador(logs, execucao);
        logs.TextoCompleto.Should().Contain("SqlState=22P02");
    }

    private static void ConferirSemMarcador(RegistroDeLogs logs, GeoImportacaoExecucao execucao)
    {
        logs.Entradas.Should().NotBeEmpty();
        logs.TextoCompleto.Should().NotContain(Marcador);
        execucao.Status.Should().Be(StatusImportacao.Falhou);
        execucao.Mensagem.Should().NotBeNullOrWhiteSpace().And.NotContain(Marcador);
    }

    private async Task<(RegistroDeLogs Logs, GeoImportacaoExecucao Execucao)> ExecutarComFalhaAsync(Exception falha)
    {
        await _banco.LimparAsync();
        using MontagemDaCarga montagem = new(_fixture);
        Guid id = await montagem.RegistrarExecucaoAsync(VersoesFicticias.VersaoNova);

        await montagem.ExecutarAsync(id, new FabricaDeFonteFixa(VersoesFicticias.Nova(falha)));

        return (montagem.Logs, await _banco.ExecucaoAsync(id));
    }
}
