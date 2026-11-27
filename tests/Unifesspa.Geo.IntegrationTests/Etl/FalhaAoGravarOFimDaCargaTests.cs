namespace Unifesspa.Geo.IntegrationTests.Etl;

using AwesomeAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using Unifesspa.Geo.Application.Abstractions;
using Unifesspa.Geo.Application.DTOs;
using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Domain.Errors;
using Unifesspa.Geo.Infrastructure.Observability;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.Infrastructure.Persistence.Etl;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;
using Unifesspa.Geo.IntegrationTests.Infrastructure;
using Unifesspa.Geo.Kernel.Results;

/// <summary>
/// Quando a gravação do fim da carga como falha também falha, a execução fica em andamento sem
/// trava. O envio seguinte a encerra como falha e é aceito, sem esperar o reinício do serviço.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class FalhaAoGravarOFimDaCargaTests : IDisposable
{
    private readonly GeoPostgisFixture _fixture;
    private readonly CenarioDaCargaPeriodica _cenario;
    private readonly EnvioDoPacoteParaTeste _envio = new();
    private readonly FalhaAoGravarOFimDaCarga[] _falha = [new FalhaAoGravarOFimDaCarga()];

    public FalhaAoGravarOFimDaCargaTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _cenario = new CenarioDaCargaPeriodica(fixture) { Interceptadores = _falha };
    }

    public void Dispose() => _envio.Dispose();

    [Fact(DisplayName = "Carga que falha e não consegue gravar a falha: o envio seguinte encerra a execução abandonada e é aceito")]
    public async Task CargaQueFalha_SemGravarAFalha_OEnvioSeguinteEncerraAAbandonada()
    {
        await _cenario.LimparAsync();
        Guid id = await _cenario.CriarExecucaoAsync("202601");
        await using GeoDbContext ctx = _cenario.CriarContexto();
        using GeoEtlMetrics metricas = new();
        CargaDaImportacao executor = CriarExecutorSemFonte(ctx, metricas);

        await executor.ExecutarAsync(await _cenario.ReservarAsync(id), CancellationToken.None);

        await ConferirEnvioSeguinteEncerraAsync(id);
    }

    [Fact(DisplayName = "Fila fechada e falha ao gravar a falha: o envio seguinte encerra a execução abandonada e é aceito")]
    public async Task FilaFechada_SemGravarAFalha_OEnvioSeguinteEncerraAAbandonada()
    {
        await _cenario.LimparAsync();
        GeoImportacaoFila fila = new();
        fila.Completar();
        await using GeoDbContext contexto = _fixture.CreateDbContext(_falha);
        RegistroDasImportacoes registro = _envio.CriarRegistro(
            contexto,
            fila,
            TravaDaCargaParaTeste.Criar(_fixture.ConnectionString),
            TimeProvider.System,
            NullLogger<RegistroDasImportacoes>.Instance);

        Result<EnvioDoPacoteRegistrado> recusado = await registro.IniciarAsync(_envio.Receber("202601").Abrir(), "teste", CancellationToken.None);

        recusado.Error!.Code.Should().Be(GeoImportacaoErrorCodes.NaoEnfileirada);
        await ConferirEnvioSeguinteEncerraAsync(await UnicaExecucaoAsync());
    }

    [Fact(DisplayName = "Esvaziamento da fila no desligamento sem gravar a falha: o envio seguinte encerra a execução abandonada e é aceito")]
    public async Task EsvaziamentoDaFila_SemGravarAFalha_OEnvioSeguinteEncerraAAbandonada()
    {
        await _cenario.LimparAsync();
        Guid id = await _cenario.CriarExecucaoAsync("202601");
        GeoImportacaoFila fila = new();
        await fila.EnfileirarAsync(await _cenario.ReservarAsync(id), CancellationToken.None);
        await using GeoDbContext ctx = _cenario.CriarContexto();
        using GeoEtlMetrics metricas = new();
        CargaDaImportacao executor = CriarExecutorSemFonte(ctx, metricas);
        using GeoImportacaoBackgroundService processo = ProcessoEmSegundoPlanoParaTeste.Criar(fila, executor, _envio.Arquivos);

        await processo.StopAsync(CancellationToken.None);

        await ConferirEnvioSeguinteEncerraAsync(id);
    }

    // Sem fonte cadastrada para a versão, a carga falha ao prepará-la.
    private CargaDaImportacao CriarExecutorSemFonte(GeoDbContext ctx, GeoEtlMetrics metricas) =>
        _cenario.CriarExecutor(
            ctx,
            new FonteFactoryFake(new Dictionary<string, IFonteDaCargaDne>(StringComparer.Ordinal)),
            new Lazy<IGeoCepCacheInvalidador>(() => Substitute.For<IGeoCepCacheInvalidador>()),
            metricas);

    private async Task ConferirEnvioSeguinteEncerraAsync(Guid abandonada)
    {
        (await ExecucaoAsync(abandonada)).Status.Should().Be(StatusImportacao.EmAndamento, "a gravação da falha não chegou ao banco");
        await ConferirTravaLivreAsync();

        Result<EnvioDoPacoteRegistrado> envio = await _envio.EnviarAsync(_fixture, _envio.Receber("202611"));

        envio.IsSuccess.Should().BeTrue("a execução abandonada não bloqueia o envio");
        GeoImportacaoExecucao encerrada = await ExecucaoAsync(abandonada);
        encerrada.Status.Should().Be(StatusImportacao.Falhou);
        encerrada.Mensagem.Should().Be(EncerramentoDasCargasAbandonadas.MensagemDaCargaAbandonada);
    }

    private async Task ConferirTravaLivreAsync()
    {
        IAsyncDisposable? trava = await TravaDaCargaParaTeste.Criar(_fixture.ConnectionString).TentarAdquirirAsync(CancellationToken.None);
        trava.Should().NotBeNull("quem encerrou a carga soltou a trava");
        await trava!.DisposeAsync();
    }

    private async Task<GeoImportacaoExecucao> ExecucaoAsync(Guid id)
    {
        await using GeoDbContext leitura = _fixture.CreateDbContext();
        return await leitura.ImportacaoExecucoes.AsNoTracking().SingleAsync(e => e.Id == id);
    }

    private async Task<Guid> UnicaExecucaoAsync()
    {
        await using GeoDbContext leitura = _fixture.CreateDbContext();
        return await leitura.ImportacaoExecucoes.Select(e => e.Id).SingleAsync();
    }
}
