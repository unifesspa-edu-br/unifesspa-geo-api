namespace Unifesspa.Geo.IntegrationTests.Etl;

using AwesomeAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using Unifesspa.Geo.Application.Abstractions;
using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Domain.Errors;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.Infrastructure.Persistence.Etl;
using Unifesspa.Geo.IntegrationTests.Infrastructure;
using Unifesspa.Geo.Kernel.Results;

/// <summary>
/// A trava consultiva da carga é obtida no próprio pedido, antes da guarda de versão e do
/// registro da execução, e fica com a carga enquanto ela estiver na fila ou em execução.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class TravaDaCargaNoPedidoTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Instante = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly GeoPostgisFixture _fixture;
    private readonly BancoDaCarga _banco;
    private readonly List<GeoImportacaoFila> _filas = [];
    private readonly List<CargaReservada> _cargasRetiradas = [];

    public TravaDaCargaNoPedidoTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _banco = new BancoDaCarga(fixture);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    // Toda carga aceita segura a trava até alguém soltá-la: mesmo que uma verificação do teste
    // falhe, nenhuma trava pode ficar presa para os testes seguintes da coleção.
    public async Task DisposeAsync()
    {
        foreach (GeoImportacaoFila fila in _filas)
        {
            await SoltarFilaAsync(fila);
        }

        foreach (CargaReservada carga in _cargasRetiradas)
        {
            await carga.DisposeAsync();
        }
    }

    [Fact(DisplayName = "Pedido com a trava ocupada recebe em_andamento, sem gravar execução nem enfileirar")]
    public async Task TravaOcupada_RecusaComEmAndamento_SemGravarNemEnfileirar()
    {
        await _banco.LimparAsync();
        GeoImportacaoFila fila = NovaFila();
        await using IAsyncDisposable outraSessao = await TravaDaCargaParaTeste.AdquirirAsync(_fixture.ConnectionString);

        Result<Guid> resultado = await IniciarAsync("202611", fila);

        resultado.IsFailure.Should().BeTrue();
        resultado.Error!.Code.Should().Be(GeoImportacaoErrorCodes.ImportacaoEmAndamento);
        (await ContarExecucoesAsync()).Should().Be(0, "a recusa vem antes de gravar a execução");
        fila.DrenarRestante().Should().BeEmpty();
    }

    [Fact(DisplayName = "Pedido aceito mantém a trava com a carga na fila até ela ser liberada")]
    public async Task PedidoAceito_MantemATravaComACargaNaFila()
    {
        await _banco.LimparAsync();
        GeoImportacaoFila fila = NovaFila();

        Result<Guid> resultado = await IniciarAsync("202611", fila);

        resultado.IsSuccess.Should().BeTrue();
        await ConferirTravaOcupadaAsync("a trava segue com a execução enfileirada");

        IReadOnlyList<CargaReservada> enfileiradas = RetirarDaFila(fila);
        enfileiradas.Should().ContainSingle().Which.ExecucaoId.Should().Be(resultado.Value);
        await enfileiradas[0].DisposeAsync();
        await ConferirTravaLivreAsync("liberar a carga solta a trava");
    }

    [Fact(DisplayName = "Partida de outra instância não toca a execução aceita na fila e, solta a trava, a marca como Falhou")]
    public async Task PartidaDeOutraInstancia_RespeitaACargaNaFila_ESoMarcaFalhouDepoisDeSoltar()
    {
        await _banco.LimparAsync();
        Guid concluida = await RegistrarConcluidaAsync("202610");
        GeoImportacaoFila fila = NovaFila();
        Guid id = (await IniciarAsync("202611", fila)).Value;
        IGeoCepCacheInvalidador cache = Substitute.For<IGeoCepCacheInvalidador>();

        await CriarPartida(cache).StartAsync(CancellationToken.None);
        (await _banco.ExecucaoAsync(id)).Status.Should().Be(StatusImportacao.EmAndamento, "a carga está na fila com a trava");
        await cache.DidNotReceiveWithAnyArgs().InvalidarAsync(default!, default);

        await SoltarFilaAsync(fila);
        await CriarPartida(cache).StartAsync(CancellationToken.None);
        (await _banco.ExecucaoAsync(id)).Status.Should().Be(StatusImportacao.Falhou, "sem a trava, a execução está abandonada");
        await cache.Received(1).InvalidarAsync(SeloDoCacheDeCep.Identificar("202610", concluida), Arg.Any<CancellationToken>());
        await cache.ReceivedWithAnyArgs(1).InvalidarAsync(default!, default);
    }

    [Fact(DisplayName = "Log que lança depois da entrega à fila não solta a trava da carga")]
    public async Task LogQueLancaDepoisDaEntrega_NaoSoltaATrava()
    {
        await _banco.LimparAsync();
        GeoImportacaoFila fila = NovaFila();

        Func<Task> pedido = () => IniciarAsync("202611", fila, new LoggerQueLanca<RegistroDasImportacoes>());

        await pedido.Should().ThrowAsync<InvalidOperationException>();
        await ConferirTravaOcupadaAsync("a carga já está com o consumidor da fila");
        RetirarDaFila(fila).Should().ContainSingle();
    }

    [Fact(DisplayName = "Recusa da guarda de versão solta a trava")]
    public async Task RecusaDaGuardaDeVersao_SoltaATrava()
    {
        await PrepararConcluidaAsync("202610");

        Result<Guid> resultado = await IniciarAsync("202609", NovaFila());

        resultado.Error!.Code.Should().Be(GeoImportacaoErrorCodes.VersaoNaoProgressiva);
        await ConferirTravaLivreAsync("a recusa não pode deixar a trava com o pedido");
    }

    [Fact(DisplayName = "Conflito no índice de execução em andamento solta a trava")]
    public async Task ConflitoNoIndiceUnico_SoltaATrava()
    {
        await _banco.LimparAsync();
        await using (GeoDbContext contexto = _fixture.CreateDbContext())
        {
            contexto.ImportacaoExecucoes.Add(GeoImportacaoExecucao.Iniciar("202610", "teste", Instante).Value!);
            await contexto.SaveChangesAsync();
        }

        Result<Guid> resultado = await IniciarAsync("202611", NovaFila());

        resultado.Error!.Code.Should().Be(GeoImportacaoErrorCodes.ImportacaoEmAndamento);
        await ConferirTravaLivreAsync("o conflito não pode deixar a trava com o pedido");
    }

    private GeoReconciliacaoStartupHostedService CriarPartida(IGeoCepCacheInvalidador cache)
    {
        ServiceCollection servicos = new();
        servicos.AddScoped(_ => _fixture.CreateDbContext());
        servicos.AddSingleton(cache);
        return new GeoReconciliacaoStartupHostedService(
            servicos.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            TravaDaCargaParaTeste.Criar(_fixture.ConnectionString),
            TimeProvider.System,
            NullLogger<GeoReconciliacaoStartupHostedService>.Instance);
    }

    private Task<Result<Guid>> IniciarAsync(string versao, GeoImportacaoFila fila) =>
        IniciarAsync(versao, fila, NullLogger<RegistroDasImportacoes>.Instance);

    private async Task<Result<Guid>> IniciarAsync(string versao, GeoImportacaoFila fila, ILogger<RegistroDasImportacoes> logger)
    {
        await using GeoDbContext contexto = _fixture.CreateDbContext();
        RegistroDasImportacoes registro = new(
            contexto,
            fila,
            TravaDaCargaParaTeste.Criar(_fixture.ConnectionString),
            TimeProvider.System,
            logger);
        return await registro.IniciarAsync(versao, "teste", CancellationToken.None);
    }

    private async Task PrepararConcluidaAsync(string versao)
    {
        await _banco.LimparAsync();
        await RegistrarConcluidaAsync(versao);
    }

    private async Task<Guid> RegistrarConcluidaAsync(string versao)
    {
        await using GeoDbContext contexto = _fixture.CreateDbContext();
        GeoImportacaoExecucao concluida = GeoImportacaoExecucao.Iniciar(versao, "teste", Instante).Value!;
        concluida.Concluir(Instante.AddMinutes(5), "{}", "concluída");
        contexto.ImportacaoExecucoes.Add(concluida);
        await contexto.SaveChangesAsync();
        return concluida.Id;
    }

    private GeoImportacaoFila NovaFila()
    {
        GeoImportacaoFila fila = new();
        _filas.Add(fila);
        return fila;
    }

    private IReadOnlyList<CargaReservada> RetirarDaFila(GeoImportacaoFila fila)
    {
        IReadOnlyList<CargaReservada> retiradas = fila.DrenarRestante();
        _cargasRetiradas.AddRange(retiradas);
        return retiradas;
    }

    private static async Task SoltarFilaAsync(GeoImportacaoFila fila)
    {
        foreach (CargaReservada enfileirada in fila.DrenarRestante())
        {
            await enfileirada.DisposeAsync();
        }
    }

    private async Task<int> ContarExecucoesAsync()
    {
        await using GeoDbContext contexto = _fixture.CreateDbContext();
        return await contexto.ImportacaoExecucoes.CountAsync();
    }

    private async Task ConferirTravaOcupadaAsync(string motivo)
    {
        IAsyncDisposable? trava = await TravaDaCargaParaTeste.Criar(_fixture.ConnectionString).TentarAdquirirAsync(CancellationToken.None);
        trava.Should().BeNull(motivo);
    }

    private async Task ConferirTravaLivreAsync(string motivo)
    {
        IAsyncDisposable? trava = await TravaDaCargaParaTeste.Criar(_fixture.ConnectionString).TentarAdquirirAsync(CancellationToken.None);
        trava.Should().NotBeNull(motivo);
        await trava!.DisposeAsync();
    }

    private sealed class LoggerQueLanca<T> : ILogger<T>
    {
        IDisposable? ILogger.BeginScope<TState>(TState state) => null;

        bool ILogger.IsEnabled(LogLevel logLevel) => true;

        void ILogger.Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            throw new InvalidOperationException("Falha simulada do registro de logs.");
    }
}
