namespace Unifesspa.Geo.IntegrationTests.Etl;

using AwesomeAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using Unifesspa.Geo.Application.Abstractions;
using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Domain.Errors;
using Unifesspa.Geo.Infrastructure.Observability;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.Infrastructure.Persistence.Etl;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;
using Unifesspa.Geo.IntegrationTests.Infrastructure;
using Unifesspa.Geo.Kernel.Results;

using static Unifesspa.Geo.IntegrationTests.Etl.CenarioDaCargaPeriodica;

/// <summary>
/// Execução em segundo plano da carga sobre Postgres+PostGIS real: o disparo enfileirado é
/// consumido e executado até concluir; no desligamento, a fila é esvaziada e as execuções que
/// não começaram ficam como falha, liberando novos disparos.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class ExecucaoEmSegundoPlanoDaCargaTests : IAsyncLifetime
{
    private readonly GeoPostgisFixture _fixture;
    private readonly CenarioDaCargaPeriodica _cenario;
    private readonly List<GeoImportacaoFila> _filas = [];

    public ExecucaoEmSegundoPlanoDaCargaTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _cenario = new CenarioDaCargaPeriodica(fixture);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    // Carga que ficou na fila segura a trava: soltá-la ao fim impede que um teste que falhou
    // no meio deixe a trava presa para os seguintes da coleção.
    public async Task DisposeAsync()
    {
        foreach (GeoImportacaoFila fila in _filas)
        {
            foreach (CargaReservada carga in fila.DrenarRestante())
            {
                await carga.DisposeAsync();
            }
        }
    }

    [Fact(DisplayName = "#694: desligamento drena a fila e marca execuções enfileiradas como Falhou, liberando novos disparos")]
    public async Task Desligamento_DrenaFila_MarcaEnfileiradasFalhou()
    {
        await _cenario.LimparAsync();
        Guid id = await _cenario.CriarExecucaoAsync("202601"); // EmAndamento, nunca processada pelo processo

        GeoImportacaoFila fila = NovaFila();
        await fila.EnfileirarAsync(await _cenario.ReservarAsync(id), CancellationToken.None);

        await using GeoDbContext ctx = _cenario.CriarContexto();
        using GeoEtlMetrics metricas = new();
        FonteFactoryFake fonteFactory = new(new Dictionary<string, IFonteDaCargaDne>(StringComparer.Ordinal));
        Lazy<IGeoCepCacheInvalidador> cacheLazy = new(() => Substitute.For<IGeoCepCacheInvalidador>());
        CargaDaImportacao executor = _cenario.CriarExecutor(ctx, fonteFactory, cacheLazy, metricas);

        using GeoImportacaoBackgroundService processo = CriarProcessoEmSegundoPlano(fila, executor);

        // Sem StartAsync: base.StopAsync retorna cedo (executeTask null) e o dreno roda —
        // simula o item enfileirado no instante imediatamente anterior ao desligamento.
        await processo.StopAsync(CancellationToken.None);

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        GeoImportacaoExecucao execucao = await leitura.ImportacaoExecucoes.SingleAsync(e => e.Id == id);
        execucao.Status.Should().Be(StatusImportacao.Falhou, "a execução enfileirada e não processada é falhada no desligamento");
        execucao.Mensagem.Should().Contain("desligamento");

        // Índice único parcial liberado: um novo disparo (mesma versão) não colide na UNIQUE.
        Guid novo = await _cenario.CriarExecucaoAsync("202601");
        novo.Should().NotBeEmpty();

        await ConferirTravaLivreAsync("a drenagem do desligamento solta a trava de cada carga");
    }

    [Fact(DisplayName = "#691: disparo com a fila fechada (serviço em desligamento) retorna NaoEnfileirada e marca a execução Falhou")]
    public async Task Disparo_ComFilaFechada_RetornaNaoEnfileirada_E_MarcaFalhou()
    {
        await _cenario.LimparAsync();

        // Writer completado: o próximo EnfileirarAsync lança ChannelClosedException — o caminho
        // que o endpoint admin mapeia para 503 (serviço em desligamento).
        GeoImportacaoFila fila = NovaFila();
        fila.Completar();

        await using GeoDbContext ctx = _cenario.CriarContexto();
        RegistroDasImportacoes registro = new(
            ctx,
            fila,
            TravaDaCargaParaTeste.Criar(_fixture.ConnectionString),
            _cenario.Relogio,
            NullLogger<RegistroDasImportacoes>.Instance);

        Result<Guid> resultado = await registro.IniciarAsync("202601", "teste", CancellationToken.None);

        resultado.IsFailure.Should().BeTrue();
        resultado.Error!.Code.Should().Be(GeoImportacaoErrorCodes.NaoEnfileirada);

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        GeoImportacaoExecucao execucao = await leitura.ImportacaoExecucoes.SingleAsync(e => e.VersaoDataset == "202601");
        execucao.Status.Should().Be(StatusImportacao.Falhou, "registrada mas não enfileirada: marcada Falhou para não bloquear o índice único parcial");
        execucao.Mensagem.Should().Contain("desligamento");

        await ConferirTravaLivreAsync("canal fechado marca Falhou e solta a trava obtida no pedido");
    }

    [Fact(DisplayName = "#692: wiring fila → processo → executor — o disparo enfileirado é consumido e executado em segundo plano até concluir")]
    public async Task Wiring_FilaProcessoEmSegundoPlanoExecutor_ProcessaAteConcluir()
    {
        await _cenario.LimparAsync();
        Guid id = await _cenario.CriarExecucaoAsync("202601");

        FonteFactoryFake fonteFactory = new(new Dictionary<string, IFonteDaCargaDne>(StringComparer.Ordinal)
        {
            ["202601"] = FonteCompleta("202601"),
        });

        GeoImportacaoFila fila = NovaFila();
        await using GeoDbContext ctx = _cenario.CriarContexto();
        using GeoEtlMetrics metricas = new();
        Lazy<IGeoCepCacheInvalidador> cacheLazy = new(() => Substitute.For<IGeoCepCacheInvalidador>());
        CargaDaImportacao carga = _cenario.CriarExecutor(ctx, fonteFactory, cacheLazy, metricas);

        // O processo resolve IGeoImportacaoExecutor de um escopo novo por item; aqui o scope
        // factory devolve este decorator, que sinaliza quando a carga termina.
        ExecutorComSinal executor = new(carga);
        using GeoImportacaoBackgroundService processo = CriarProcessoEmSegundoPlano(fila, executor);

        await fila.EnfileirarAsync(await _cenario.ReservarAsync(id), CancellationToken.None);
        await processo.StartAsync(CancellationToken.None);

        // Espera determinística (sem Sleep): o sinal dispara no finally do ExecutarAsync, após
        // a carga concluir. WaitAsync limita o tempo para não pendurar o CI se o wiring quebrar.
        await executor.Concluido.WaitAsync(TimeSpan.FromSeconds(30));

        await processo.StopAsync(CancellationToken.None);

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        GeoImportacaoExecucao execucao = await leitura.ImportacaoExecucoes.SingleAsync(e => e.Id == id);
        execucao.Status.Should().Be(StatusImportacao.Concluida, "o processo consumiu a fila e o executor rodou a carga até concluir");
        execucao.ConcluidoEm.Should().NotBeNull();

        await ConferirTravaLivreAsync("o item da fila solta a trava ao fim da carga");
    }

    [Fact(DisplayName = "Item da fila cujo executor lança exceção marca a execução como Falhou e solta a trava")]
    public async Task ItemDaFila_ExecutorLancaExcecao_MarcaFalhouESoltaATrava()
    {
        await _cenario.LimparAsync();
        Guid id = await _cenario.CriarExecucaoAsync("202601");
        GeoImportacaoFila fila = NovaFila();
        await using GeoDbContext ctx = _cenario.CriarContexto();
        using GeoEtlMetrics metricas = new();
        Lazy<IGeoCepCacheInvalidador> cacheLazy = new(() => Substitute.For<IGeoCepCacheInvalidador>());
        CargaDaImportacao real = _cenario.CriarExecutor(
            ctx, new FonteFactoryFake(new Dictionary<string, IFonteDaCargaDne>(StringComparer.Ordinal)), cacheLazy, metricas);
        ExecutorComSinal executor = new(new ExecutorQueFalha(real));
        using GeoImportacaoBackgroundService processo = CriarProcessoEmSegundoPlano(fila, executor);

        await fila.EnfileirarAsync(await _cenario.ReservarAsync(id), CancellationToken.None);
        await processo.StartAsync(CancellationToken.None);
        await executor.Concluido.WaitAsync(TimeSpan.FromSeconds(30));
        await processo.StopAsync(CancellationToken.None);

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        GeoImportacaoExecucao execucao = await leitura.ImportacaoExecucoes.SingleAsync(e => e.Id == id);
        execucao.Status.Should().Be(StatusImportacao.Falhou, "a execução não pode ficar EmAndamento sem trava");
        await ConferirTravaLivreAsync("a falha do executor não pode manter a trava");
    }

    private GeoImportacaoFila NovaFila()
    {
        GeoImportacaoFila fila = new();
        _filas.Add(fila);
        return fila;
    }

    private async Task ConferirTravaLivreAsync(string motivo)
    {
        IAsyncDisposable? trava = await TravaDaCargaParaTeste.Criar(_fixture.ConnectionString).TentarAdquirirAsync(CancellationToken.None);
        trava.Should().NotBeNull(motivo);
        await trava!.DisposeAsync();
    }

    private static GeoImportacaoBackgroundService CriarProcessoEmSegundoPlano(IGeoImportacaoFila fila, IGeoImportacaoExecutor executor)
    {
        // Scope factory mínimo: o dreno do StopAsync resolve IGeoImportacaoExecutor do escopo.
        IServiceProvider sp = Substitute.For<IServiceProvider>();
        sp.GetService(typeof(IGeoImportacaoExecutor)).Returns(executor);
        IServiceScope escopo = Substitute.For<IServiceScope>();
        escopo.ServiceProvider.Returns(sp);
        IServiceScopeFactory scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(escopo);

        return new GeoImportacaoBackgroundService(
            scopeFactory,
            fila,
            NullLogger<GeoImportacaoBackgroundService>.Instance);
    }

    // Decora o executor sinalizando o fim de ExecutarAsync — permite ao teste do wiring
    // (#692) aguardar a execução em segundo plano de forma determinística, sem Sleep.
    private sealed class ExecutorComSinal : IGeoImportacaoExecutor
    {
        private readonly IGeoImportacaoExecutor _interno;
        private readonly TaskCompletionSource _concluido = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ExecutorComSinal(IGeoImportacaoExecutor interno)
        {
            _interno = interno;
        }

        public Task Concluido => _concluido.Task;

        public async Task ExecutarAsync(CargaReservada carga, CancellationToken cancellationToken)
        {
            try
            {
                await _interno.ExecutarAsync(carga, cancellationToken);
            }
            finally
            {
                _concluido.TrySetResult();
            }
        }

        public Task MarcarInterrompidaNoDesligamentoAsync(Guid execucaoId, CancellationToken cancellationToken) =>
            _interno.MarcarInterrompidaNoDesligamentoAsync(execucaoId, cancellationToken);

        public Task MarcarFalhaInesperadaAsync(Guid execucaoId, CancellationToken cancellationToken) =>
            _interno.MarcarFalhaInesperadaAsync(execucaoId, cancellationToken);
    }

    private sealed class ExecutorQueFalha : IGeoImportacaoExecutor
    {
        private readonly IGeoImportacaoExecutor _real;

        public ExecutorQueFalha(IGeoImportacaoExecutor real)
        {
            _real = real;
        }

        public Task ExecutarAsync(CargaReservada carga, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Falha simulada do executor.");

        public Task MarcarInterrompidaNoDesligamentoAsync(Guid execucaoId, CancellationToken cancellationToken) =>
            _real.MarcarInterrompidaNoDesligamentoAsync(execucaoId, cancellationToken);

        public Task MarcarFalhaInesperadaAsync(Guid execucaoId, CancellationToken cancellationToken) =>
            _real.MarcarFalhaInesperadaAsync(execucaoId, cancellationToken);
    }
}
