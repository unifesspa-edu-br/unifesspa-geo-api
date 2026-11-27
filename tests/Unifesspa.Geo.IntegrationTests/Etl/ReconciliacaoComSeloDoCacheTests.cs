namespace Unifesspa.Geo.IntegrationTests.Etl;

using AwesomeAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using Unifesspa.Geo.Application.Abstractions;
using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.Infrastructure.Persistence.Etl;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Na partida, com a trava livre a execução órfã vai a falha e o selo do cache de CEP é
/// sincronizado, sob a trava, com a última execução concluída (a mais recente por data de
/// conclusão). Com a trava ocupada, nada é tocado e o selo não é gravado: quem a detém sela
/// ao confirmar.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class ReconciliacaoComSeloDoCacheTests
{
    private static readonly DateTimeOffset Instante = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly GeoPostgisFixture _fixture;
    private readonly BancoDaCarga _banco;
    private readonly IGeoCepCacheInvalidador _cache = Substitute.For<IGeoCepCacheInvalidador>();

    public ReconciliacaoComSeloDoCacheTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _banco = new BancoDaCarga(fixture);
    }

    [Fact(DisplayName = "Com a trava livre, a órfã vai a falha e o selo do cache acompanha a última execução concluída")]
    public async Task TravaLivre_FalhaAOrfaESincronizaSelo()
    {
        (Guid orfa, Guid antiga, Guid recente) = await PrepararHistoricoAsync();

        await CriarServico().StartAsync(CancellationToken.None);

        (await _banco.ExecucaoAsync(orfa)).Status.Should().Be(StatusImportacao.Falhou);
        await ConferirSeloAsync(recente, antiga);
    }

    [Fact(DisplayName = "Com a trava ocupada, a execução viva não é tocada e o selo do cache não é gravado")]
    public async Task TravaOcupada_PreservaVivaESemGravarSelo()
    {
        (Guid viva, _, _) = await PrepararHistoricoAsync();

        await using (await TravaDaCargaParaTeste.AdquirirAsync(_fixture.ConnectionString))
        {
            await CriarServico().StartAsync(CancellationToken.None);
        }

        (await _banco.ExecucaoAsync(viva)).Status.Should().Be(StatusImportacao.EmAndamento);
        await _cache.DidNotReceiveWithAnyArgs().InvalidarAsync(default!, default);
    }

    [Fact(DisplayName = "O selo do cache é gravado enquanto a trava da partida ainda está retida")]
    public async Task TravaLivre_SelaOCacheSobATrava()
    {
        await PrepararHistoricoAsync();
        bool travaRetidaAoSelar = false;
        _cache.InvalidarAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            travaRetidaAoSelar = await TravaEstaRetidaAsync();
        });

        await CriarServico().StartAsync(CancellationToken.None);

        travaRetidaAoSelar.Should().BeTrue("a sincronização do selo roda dentro da seção com a trava");
    }

    [Fact(DisplayName = "Falha ao descartar os esquemas na partida não a interrompe: o diretório é limpo, o selo é gravado e a órfã segue em andamento até a próxima partida")]
    public async Task FalhaNoDescarte_NaoInterrompeAPartida_EAOrfaSegueEmAndamento()
    {
        (Guid orfa, _, Guid recente) = await PrepararHistoricoAsync();
        using DiretorioDePacotesTemporario pacotes = new();
        await pacotes.DeixarArquivoResidualAsync();
        FonteQueFalhaAoDescartarSobras descarteComFalha = new(new InvalidOperationException("Falha simulada no descarte."));

        await CriarServico(descarteComFalha, pacotes.Arquivos).StartAsync(CancellationToken.None);

        pacotes.EstaVazio.Should().BeTrue();
        await _cache.Received(1).InvalidarAsync($"202603.{recente:N}", Arg.Any<CancellationToken>());
        (await _banco.ExecucaoAsync(orfa)).Status.Should().Be(StatusImportacao.EmAndamento);

        await CriarServico(new FabricaDeFonteFixa(), pacotes.Arquivos).StartAsync(CancellationToken.None);

        (await _banco.ExecucaoAsync(orfa)).Mensagem.Should().Be(EncerramentoDasCargasAbandonadas.MensagemDaCargaAbandonada);
    }

    private async Task<bool> TravaEstaRetidaAsync()
    {
        IAsyncDisposable? outra = await CriarTrava().TentarAdquirirAsync(CancellationToken.None);
        if (outra is not null)
        {
            await outra.DisposeAsync();
        }

        return outra is null;
    }

    private async Task ConferirSeloAsync(Guid recente, Guid antiga)
    {
        await _cache.Received(1).InvalidarAsync($"202603.{recente:N}", Arg.Any<CancellationToken>());
        await _cache.DidNotReceive().InvalidarAsync($"202602.{antiga:N}", Arg.Any<CancellationToken>());
    }

    private async Task<(Guid EmAndamento, Guid ConcluidaAntiga, Guid ConcluidaRecente)> PrepararHistoricoAsync()
    {
        await _banco.LimparAsync();
        await using GeoDbContext contexto = _fixture.CreateDbContext();
        GeoImportacaoExecucao antiga = Concluida("202602", Instante);
        GeoImportacaoExecucao recente = Concluida("202603", Instante.AddDays(30));
        GeoImportacaoExecucao andamento = GeoImportacaoExecucao.Iniciar(PacotesDeTeste.Recebido("202604"), "teste", Instante.AddDays(60)).Value!;
        contexto.ImportacaoExecucoes.AddRange(antiga, recente, andamento);
        await contexto.SaveChangesAsync();
        return (andamento.Id, antiga.Id, recente.Id);
    }

    private static GeoImportacaoExecucao Concluida(string versao, DateTimeOffset iniciadaEm)
    {
        GeoImportacaoExecucao execucao = GeoImportacaoExecucao.Iniciar(PacotesDeTeste.Recebido(versao), "teste", iniciadaEm).Value!;
        execucao.Concluir(iniciadaEm.AddMinutes(10), "{}", "concluída");
        return execucao;
    }

    private GeoReconciliacaoStartupHostedService CriarServico() =>
        CriarServico(new FabricaDeFonteFixa(), new ArquivoDoPacoteDne(Path.Combine(Path.GetTempPath(), $"pacotes-dne-{Guid.NewGuid():N}")));

    private GeoReconciliacaoStartupHostedService CriarServico(IFonteDaExecucaoDne fonte, ArquivoDoPacoteDne arquivos) =>
        new(
            ServicosDaPartidaParaTeste.Criar(_fixture, fonte, servicos => servicos.AddSingleton(_cache)),
            CriarTrava(),
            arquivos,
            NullLogger<GeoReconciliacaoStartupHostedService>.Instance);

    private TravaConsultivaDaCargaDne CriarTrava()
    {
        return TravaDaCargaParaTeste.Criar(_fixture.ConnectionString);
    }
}
