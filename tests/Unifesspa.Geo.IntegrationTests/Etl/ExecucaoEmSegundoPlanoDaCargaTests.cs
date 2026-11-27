namespace Unifesspa.Geo.IntegrationTests.Etl;

using AwesomeAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

using static Unifesspa.Geo.IntegrationTests.Etl.CenarioDaCargaPeriodica;

/// <summary>
/// Execução em segundo plano da carga sobre Postgres+PostGIS real: o disparo enfileirado é
/// consumido e executado até concluir; no desligamento, a fila é esvaziada e as execuções que
/// não começaram ficam como falha, liberando novos disparos.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class ExecucaoEmSegundoPlanoDaCargaTests : IAsyncLifetime, IDisposable
{
    private readonly GeoPostgisFixture _fixture;
    private readonly CenarioDaCargaPeriodica _cenario;
    private readonly EnvioDoPacoteParaTeste _envio = new();
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

    public void Dispose() => _envio.Dispose();

    [Fact(DisplayName = "#694: desligamento drena a fila e marca execuções enfileiradas como Falhou, liberando novos disparos")]
    public async Task Desligamento_DrenaFila_MarcaEnfileiradasFalhou()
    {
        await _cenario.LimparAsync();
        Guid id = await _cenario.CriarExecucaoAsync("202601"); // EmAndamento, nunca processada pelo processo
        GeoImportacaoFila fila = await FilaComExecucaoEnfileiradaAsync(id);
        await using GeoDbContext ctx = _cenario.CriarContexto();
        using GeoEtlMetrics metricas = new();
        CargaDaImportacao executor = CriarExecutor(ctx, metricas, new FabricaDeFonteFixa());
        using GeoImportacaoBackgroundService processo = CriarProcessoEmSegundoPlano(fila, executor);

        // Sem StartAsync: base.StopAsync retorna cedo (executeTask null) e o dreno roda —
        // simula o item enfileirado no instante imediatamente anterior ao desligamento.
        await processo.StopAsync(CancellationToken.None);

        await ConferirFalhaNoDesligamentoENovoDisparoLivreAsync(id);
        await ConferirTravaLivreAsync("a drenagem do desligamento solta a trava de cada carga");
    }

    [Fact(DisplayName = "#691: disparo com a fila fechada (serviço em desligamento) retorna NaoEnfileirada e marca a execução Falhou")]
    public async Task Disparo_ComFilaFechada_RetornaNaoEnfileirada_E_MarcaFalhou()
    {
        await _cenario.LimparAsync();
        GeoImportacaoFila fila = NovaFilaFechada();

        Result<EnvioDoPacoteRegistrado> resultado = await IniciarPedidoAsync(fila);

        resultado.IsFailure.Should().BeTrue();
        resultado.Error!.Code.Should().Be(GeoImportacaoErrorCodes.NaoEnfileirada);

        await ConferirExecucaoDoPedidoFalhouPorFilaFechadaAsync();
        _envio.NomesDosArquivos().Should().BeEmpty("o pacote não enfileirado é apagado");
        await ConferirTravaLivreAsync("canal fechado marca Falhou e solta a trava obtida no pedido");
    }

    [Fact(DisplayName = "#692: wiring fila → processo → executor — o disparo enfileirado é consumido e executado em segundo plano até concluir")]
    public async Task Wiring_FilaProcessoEmSegundoPlanoExecutor_ProcessaAteConcluir()
    {
        await _cenario.LimparAsync();
        Guid id = await _cenario.CriarExecucaoAsync("202601");
        GeoImportacaoFila fila = NovaFila();
        await using GeoDbContext ctx = _cenario.CriarContexto();
        using GeoEtlMetrics metricas = new();
        CargaDaImportacao carga = CriarExecutor(ctx, metricas, new FabricaDeFonteFixa(FonteCompleta("202601")));
        ExecutorComSinal executor = new(carga);
        using GeoImportacaoBackgroundService processo = CriarProcessoEmSegundoPlano(fila, executor);

        await ProcessarItemAteOFimAsync(processo, executor, fila, id);

        await ConferirExecucaoConcluidaAsync(id);
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
        CargaDaImportacao real = CriarExecutor(ctx, metricas, new FabricaDeFonteFixa());
        ExecutorComSinal executor = new(new ExecutorQueFalha(real));
        using GeoImportacaoBackgroundService processo = CriarProcessoEmSegundoPlano(fila, executor);

        await ProcessarItemAteOFimAsync(processo, executor, fila, id);

        await ConferirExecucaoFalhouSemTravaAsync(id);
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

    // Writer completado: o próximo EnfileirarAsync lança ChannelClosedException — o caminho
    // que o endpoint admin mapeia para 503 (serviço em desligamento).
    private GeoImportacaoFila NovaFilaFechada()
    {
        GeoImportacaoFila fila = NovaFila();
        fila.Completar();
        return fila;
    }

    private async Task<GeoImportacaoFila> FilaComExecucaoEnfileiradaAsync(Guid execucaoId)
    {
        GeoImportacaoFila fila = NovaFila();
        await fila.EnfileirarAsync(await _cenario.ReservarAsync(execucaoId), CancellationToken.None);
        return fila;
    }

    private CargaDaImportacao CriarExecutor(GeoDbContext ctx, GeoEtlMetrics metricas, FabricaDeFonteFixa fonteFactory)
    {
        Lazy<IGeoCepCacheInvalidador> cacheLazy = new(() => Substitute.For<IGeoCepCacheInvalidador>());
        return _cenario.CriarExecutor(ctx, fonteFactory, cacheLazy, metricas);
    }

    private async Task<Result<EnvioDoPacoteRegistrado>> IniciarPedidoAsync(GeoImportacaoFila fila)
    {
        await using GeoDbContext ctx = _cenario.CriarContexto();
        RegistroDasImportacoes registro = _envio.CriarRegistro(
            ctx,
            fila,
            TravaDaCargaParaTeste.Criar(_fixture.ConnectionString),
            _cenario.Relogio,
            NullLogger<RegistroDasImportacoes>.Instance);

        return await registro.IniciarAsync(_envio.Receber("202601").Abrir(), "teste", CancellationToken.None);
    }

    private async Task ProcessarItemAteOFimAsync(GeoImportacaoBackgroundService processo, ExecutorComSinal executor, GeoImportacaoFila fila, Guid execucaoId)
    {
        await fila.EnfileirarAsync(await _cenario.ReservarAsync(execucaoId), CancellationToken.None);
        await processo.StartAsync(CancellationToken.None);

        // Espera determinística (sem Sleep): o sinal dispara no finally do ExecutarAsync, após
        // a carga concluir. WaitAsync limita o tempo para não pendurar o CI se o wiring quebrar.
        await executor.Concluido.WaitAsync(TimeSpan.FromSeconds(30));

        await processo.StopAsync(CancellationToken.None);
    }

    private async Task ConferirFalhaNoDesligamentoENovoDisparoLivreAsync(Guid id)
    {
        await using GeoDbContext leitura = _fixture.CreateDbContext();
        GeoImportacaoExecucao execucao = await leitura.ImportacaoExecucoes.SingleAsync(e => e.Id == id);
        execucao.Status.Should().Be(StatusImportacao.Falhou, "a execução enfileirada e não processada é falhada no desligamento");
        execucao.Mensagem.Should().Contain("desligamento");

        // Índice único parcial liberado: um novo disparo (mesma versão) não colide na UNIQUE.
        Guid novo = await _cenario.CriarExecucaoAsync("202601");
        novo.Should().NotBeEmpty();
    }

    private async Task ConferirExecucaoDoPedidoFalhouPorFilaFechadaAsync()
    {
        await using GeoDbContext leitura = _fixture.CreateDbContext();
        GeoImportacaoExecucao execucao = await leitura.ImportacaoExecucoes.SingleAsync(e => e.VersaoDataset == "202601");
        execucao.Status.Should().Be(StatusImportacao.Falhou, "registrada mas não enfileirada: marcada Falhou para não bloquear o índice único parcial");
        execucao.Mensagem.Should().Contain("desligamento");
    }

    private async Task ConferirExecucaoConcluidaAsync(Guid id)
    {
        await using GeoDbContext leitura = _fixture.CreateDbContext();
        GeoImportacaoExecucao execucao = await leitura.ImportacaoExecucoes.SingleAsync(e => e.Id == id);
        execucao.Status.Should().Be(StatusImportacao.Concluida, "o processo consumiu a fila e o executor rodou a carga até concluir");
        execucao.ConcluidoEm.Should().NotBeNull();
    }

    private async Task ConferirExecucaoFalhouSemTravaAsync(Guid id)
    {
        await using GeoDbContext leitura = _fixture.CreateDbContext();
        GeoImportacaoExecucao execucao = await leitura.ImportacaoExecucoes.SingleAsync(e => e.Id == id);
        execucao.Status.Should().Be(StatusImportacao.Falhou, "a execução não pode ficar EmAndamento sem trava");
    }

    private GeoImportacaoBackgroundService CriarProcessoEmSegundoPlano(IGeoImportacaoFila fila, IGeoImportacaoExecutor executor) =>
        ProcessoEmSegundoPlanoParaTeste.Criar(fila, executor, _envio.Arquivos);

    // Decora o executor sinalizando o fim de ExecutarAsync — permite ao teste do wiring
    // aguardar a execução em segundo plano de forma determinística, sem Sleep. O processo
    // resolve o executor de um escopo novo por item; aqui o scope factory devolve este decorator.
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
