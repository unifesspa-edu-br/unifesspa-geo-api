namespace Unifesspa.Geo.IntegrationTests.Etl;

using AwesomeAssertions;

using Microsoft.EntityFrameworkCore;

using Unifesspa.Geo.Application.DTOs;
using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.IntegrationTests.Infrastructure;
using Unifesspa.Geo.Kernel.Results;

/// <summary>
/// O pacote só é repetido quando o resumo SHA-256 é o da última carga concluída: devolve essa
/// execução, apaga o arquivo e não registra carga nova. O arquivo aceito passa a ter o identificador
/// da execução e o registro guarda o resumo e o tamanho.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class RepeticaoDoPacoteTests : IDisposable
{
    private static readonly DateTimeOffset Instante = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly GeoPostgisFixture _fixture;
    private readonly BancoDaCarga _banco;
    private readonly EnvioDoPacoteParaTeste _envio = new();

    public RepeticaoDoPacoteTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _banco = new BancoDaCarga(fixture);
    }

    public void Dispose() => _envio.Dispose();

    [Fact(DisplayName = "Mesmo pacote da última carga concluída devolve essa execução, sem carga nova, e apaga o arquivo")]
    public async Task MesmoPacoteDaUltimaConcluida_DevolveAExecucaoSemCargaNova()
    {
        await _banco.LimparAsync();
        Guid concluida = await RegistrarAsync("202610", 'b', Instante, StatusImportacao.Concluida);

        Result<EnvioDoPacoteRegistrado> resultado = await _envio.EnviarAsync(_fixture, _envio.Receber("202610", 'b'));

        resultado.Value!.Desfecho.Should().Be(DesfechoDoEnvioDoPacote.JaPublicado);
        resultado.Value.ExecucaoId.Should().Be(concluida);
        (await ContarExecucoesAsync()).Should().Be(1);
        _envio.NomesDosArquivos().Should().BeEmpty();
    }

    [Fact(DisplayName = "A → B → A: reenviar A depois de B concluída inicia nova carga; reenviar B devolve a vigente")]
    public async Task CorrecaoDaMesmaVersao_SoRepeteQuandoOResumoEDaUltimaConcluida()
    {
        await _banco.LimparAsync();
        await RegistrarAsync("202610", 'a', Instante, StatusImportacao.Concluida);
        Guid b = await RegistrarAsync("202610", 'b', Instante.AddDays(1), StatusImportacao.Concluida);

        Result<EnvioDoPacoteRegistrado> reenvioDeB = await _envio.EnviarAsync(_fixture, _envio.Receber("202610", 'b'));
        reenvioDeB.Value!.Desfecho.Should().Be(DesfechoDoEnvioDoPacote.JaPublicado);
        reenvioDeB.Value.ExecucaoId.Should().Be(b);

        Result<EnvioDoPacoteRegistrado> reenvioDeA = await _envio.EnviarAsync(_fixture, _envio.Receber("202610", 'a'));
        reenvioDeA.Value!.Desfecho.Should().Be(DesfechoDoEnvioDoPacote.CargaEnfileirada);
        reenvioDeA.Value.ExecucaoId.Should().NotBe(b);
    }

    [Fact(DisplayName = "Mesmo resumo só em execução que falhou inicia nova carga")]
    public async Task MesmoPacoteQueSoFalhou_IniciaNovaCarga()
    {
        await _banco.LimparAsync();
        await RegistrarAsync("202610", 'a', Instante, StatusImportacao.Falhou);

        Result<EnvioDoPacoteRegistrado> resultado = await _envio.EnviarAsync(_fixture, _envio.Receber("202610", 'a'));

        resultado.Value!.Desfecho.Should().Be(DesfechoDoEnvioDoPacote.CargaEnfileirada);
    }

    [Fact(DisplayName = "Última carga concluída sem resumo registrado não é tomada como repetição")]
    public async Task UltimaConcluidaSemResumo_NaoEhRepeticao()
    {
        await _banco.LimparAsync();
        await RegistrarSemPacoteAsync("202610");

        Result<EnvioDoPacoteRegistrado> resultado = await _envio.EnviarAsync(_fixture, _envio.Receber("202610", 'a'));

        resultado.Value!.Desfecho.Should().Be(DesfechoDoEnvioDoPacote.CargaEnfileirada);
    }

    [Fact(DisplayName = "Pacote aceito: o arquivo passa a ter o identificador da execução e o registro guarda o resumo e o tamanho")]
    public async Task PacoteAceito_ArquivoRecebeOIdentificadorEORegistroGuardaOResumo()
    {
        await _banco.LimparAsync();
        ZipDeTeste pacote = _envio.Receber("202611", 'd');

        Result<EnvioDoPacoteRegistrado> resultado = await _envio.EnviarAsync(_fixture, pacote);

        Guid id = resultado.Value!.ExecucaoId;
        _envio.NomesDosArquivos().Should().ContainSingle().Which.Should().Be(id.ToString("N"));
        await using GeoDbContext contexto = _fixture.CreateDbContext();
        GeoImportacaoExecucao execucao = await contexto.ImportacaoExecucoes.SingleAsync(e => e.Id == id);
        execucao.PacoteSha256.Should().Be(pacote.Sha256);
        execucao.PacoteTamanhoEmBytes.Should().Be(pacote.TamanhoEmBytes);
        execucao.VersaoDataset.Should().Be("202611");
    }

    private async Task<Guid> RegistrarAsync(string versao, char resumo, DateTimeOffset iniciadoEm, StatusImportacao status)
    {
        GeoImportacaoExecucao execucao = GeoImportacaoExecucao.Iniciar(_envio.Recebido(versao, resumo), "teste", iniciadoEm).Value!;
        _ = status == StatusImportacao.Concluida
            ? execucao.Concluir(iniciadoEm.AddMinutes(5), "{}", "concluída")
            : execucao.Falhar(iniciadoEm.AddMinutes(5), "falha simulada");
        await GravarAsync(execucao);
        return execucao.Id;
    }

    private async Task RegistrarSemPacoteAsync(string versao)
    {
        await using GeoDbContext contexto = _fixture.CreateDbContext();
        GeoImportacaoExecucao execucao = GeoImportacaoExecucao.Iniciar(PacotesDeTeste.Recebido(versao), "teste", Instante).Value!;
        execucao.Concluir(Instante.AddMinutes(5), "{}", "concluída");
        contexto.ImportacaoExecucoes.Add(execucao);
        await contexto.SaveChangesAsync();
        await contexto.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE geo_importacao_execucao SET pacote_sha256 = NULL, pacote_tamanho_em_bytes = NULL WHERE id = {execucao.Id}");
    }

    private async Task GravarAsync(GeoImportacaoExecucao execucao)
    {
        await using GeoDbContext contexto = _fixture.CreateDbContext();
        contexto.ImportacaoExecucoes.Add(execucao);
        await contexto.SaveChangesAsync();
    }

    private async Task<int> ContarExecucoesAsync()
    {
        await using GeoDbContext contexto = _fixture.CreateDbContext();
        return await contexto.ImportacaoExecucoes.CountAsync();
    }
}
