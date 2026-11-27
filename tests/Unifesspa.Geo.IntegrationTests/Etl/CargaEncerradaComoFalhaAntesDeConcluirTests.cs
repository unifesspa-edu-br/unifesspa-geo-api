namespace Unifesspa.Geo.IntegrationTests.Etl;

using AwesomeAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using Unifesspa.Geo.Application.Abstractions;
using Unifesspa.Geo.Application.DTOs;
using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.Infrastructure.Persistence.Etl;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;
using Unifesspa.Geo.IntegrationTests.Infrastructure;
using Unifesspa.Geo.Kernel.Results;

/// <summary>
/// Quando o banco solta a trava com a carga ainda viva e outro encerramento marca a execução como
/// falha, a conclusão da carga antiga é recusada e a publicação dela é desfeita. No sentido
/// contrário, se a carga já gravou a conclusão, o encerramento não grava falha por cima.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class CargaEncerradaComoFalhaAntesDeConcluirTests : IDisposable
{
    private const string VersaoDoEnvioSeguinte = "202603";

    private readonly GeoPostgisFixture _fixture;
    private readonly BancoDaCarga _banco;
    private readonly EnvioDoPacoteParaTeste _envio = new();
    private Task<Result<EnvioDoPacoteRegistrado>>? _envioEmSegundoPlano;

    public CargaEncerradaComoFalhaAntesDeConcluirTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _banco = new BancoDaCarga(fixture);
    }

    public void Dispose() => _envio.Dispose();

    [Fact(DisplayName = "Carga encerrada como falha pelo envio seguinte tem a conclusão recusada, a publicação desfeita e a falha mantida")]
    public async Task EnvioSeguinte_EncerraACargaViva_ConclusaoRecusadaDesfazAPublicacao()
    {
        ZipDeTeste pacote = _envio.Receber(VersoesFicticias.VersaoNova);
        Result<EnvioDoPacoteRegistrado>? durante = null;
        using MontagemDaCarga montagem = MontarQueDerrubaATrava(async () => durante = await _envio.EnviarAsync(_fixture, pacote));

        Guid id = await ExecutarVersaoNovaAsync(montagem);

        durante!.IsSuccess.Should().BeTrue("o envio encontra a trava livre e encerra a execução abandonada");
        await ConferirFalhaMantidaEBaseAnteriorAsync(id);
        montagem.Logs.TextoCompleto.Should().Contain($"conclusão da carga {id}");
        await montagem.Cache.DidNotReceiveWithAnyArgs().InvalidarAsync(default!, default);
        (await _envio.EnviarAsync(_fixture, pacote)).IsSuccess.Should().BeTrue("o mesmo pacote pode ser reenviado");
    }

    [Fact(DisplayName = "Carga encerrada como falha na partida do serviço tem a conclusão recusada e a falha mantida")]
    public async Task Partida_EncerraACargaViva_ConclusaoRecusadaMantemAFalha()
    {
        IGeoCepCacheInvalidador cacheDaPartida = Substitute.For<IGeoCepCacheInvalidador>();
        using MontagemDaCarga montagem = MontarQueDerrubaATrava(() => CriarPartida(cacheDaPartida).StartAsync(CancellationToken.None));

        Guid id = await ExecutarVersaoNovaAsync(montagem);

        await ConferirFalhaMantidaEBaseAnteriorAsync(id);
        await montagem.Cache.DidNotReceiveWithAnyArgs().InvalidarAsync(default!, default);
        await cacheDaPartida.DidNotReceive().InvalidarAsync($"{VersoesFicticias.VersaoNova}.{id:N}", Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Encerramento que espera a conclusão da carga não grava falha por cima e o envio segue")]
    public async Task EncerramentoAguardandoAConclusao_NaoGravaFalha_EOEnvioSegue()
    {
        await _banco.LimparAsync();
        using MontagemDaCarga montagem = new(_fixture);
        Guid id = await montagem.RegistrarExecucaoAsync(VersoesFicticias.VersaoAnterior);
        await using GeoDbContext contexto = _fixture.CreateDbContext([AoConfirmarDerrubarATravaEEnviar()]);

        await montagem.ExecutarAsync(id, new FabricaDeFonteFixa(VersoesFicticias.Anterior()), contexto);

        Result<EnvioDoPacoteRegistrado> resultado = await _envioEmSegundoPlano!;
        resultado.IsSuccess.Should().BeTrue("o encerramento não encontrou execução a falhar e o envio segue");
        (await _banco.ExecucaoAsync(id)).Status.Should().Be(StatusImportacao.Concluida);
        await montagem.Cache.Received(1).InvalidarAsync($"{VersoesFicticias.VersaoAnterior}.{id:N}", Arg.Any<CancellationToken>());
    }

    // Com a gravação da conclusão já feita e ainda não confirmada, o envio encerra a execução em
    // segundo plano e a confirmação só segue quando essa gravação está aguardando o bloqueio da linha.
    private ObservadorDaConfirmacao AoConfirmarDerrubarATravaEEnviar() =>
        new(async _ =>
        {
            await QuedaDaConexaoDaTrava.DerrubarAsync(_fixture.ConnectionString);
            _envioEmSegundoPlano = Task.Run(() => _envio.EnviarAsync(_fixture, _envio.Receber(VersaoDoEnvioSeguinte)));
            await EsperaPeloBloqueioNoBanco.GravacaoDaExecucaoAguardandoAsync(_fixture.ConnectionString);
        });

    private MontagemDaCarga MontarQueDerrubaATrava(Func<Task> depoisDaQueda) =>
        new(_fixture)
        {
            DecorarFolhas = folhas => new FolhasComGancho(folhas, async () =>
            {
                await QuedaDaConexaoDaTrava.DerrubarAsync(_fixture.ConnectionString);
                await depoisDaQueda();
            }),
        };

    private async Task<Guid> ExecutarVersaoNovaAsync(MontagemDaCarga montagem)
    {
        await _banco.LimparAsync();
        using (MontagemDaCarga anterior = new(_fixture))
        {
            await anterior.PublicarAsync(VersoesFicticias.Anterior());
        }

        Guid id = await montagem.RegistrarExecucaoAsync(VersoesFicticias.VersaoNova);
        await montagem.ExecutarAsync(id, new FabricaDeFonteFixa(VersoesFicticias.Nova()));
        return id;
    }

    private async Task ConferirFalhaMantidaEBaseAnteriorAsync(Guid id)
    {
        GeoImportacaoExecucao execucao = await _banco.ExecucaoAsync(id);
        execucao.Status.Should().Be(StatusImportacao.Falhou);
        execucao.Mensagem.Should().Be(EncerramentoDasCargasAbandonadas.MensagemDaCargaAbandonada);
        await _banco.ConferirSomenteVersaoAnteriorAsync();
    }

    private GeoReconciliacaoStartupHostedService CriarPartida(IGeoCepCacheInvalidador cache) =>
        new(
            ServicosDaPartidaParaTeste.Criar(_fixture, new FabricaDeFonteFixa(), servicos => servicos.AddSingleton(cache)),
            TravaDaCargaParaTeste.Criar(_fixture.ConnectionString),
            new ArquivoDoPacoteDne(Path.Combine(Path.GetTempPath(), $"pacotes-dne-{Guid.NewGuid():N}")),
            NullLogger<GeoReconciliacaoStartupHostedService>.Instance);
}
