namespace Unifesspa.Geo.IntegrationTests.Etl;

using AwesomeAssertions;

using NSubstitute;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Falha em qualquer ponto da carga desfaz tudo: a versão anterior continua publicada, sem
/// rastro da seguinte, e a execução fica como falha. A versão seguinte não traz São Paulo e
/// traz um complemento e um logradouro novos; se algo fosse confirmado, apareceria no banco.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class ReversaoIntegralDaCargaTests
{
    private readonly GeoPostgisFixture _fixture;
    private readonly BancoDaCarga _banco;

    public ReversaoIntegralDaCargaTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _banco = new BancoDaCarga(fixture);
    }

    [Fact(DisplayName = "Falha depois da cópia dos complementos desfaz a carga e mantém a versão anterior publicada")]
    public async Task FalhaDepoisDaCopiaDosComplementos_DesfazTudo()
    {
        using MontagemDaCarga montagem = new(_fixture);
        FonteEmMemoria nova = VersoesFicticias.Nova(new InvalidOperationException("Falha simulada na leitura dos logradouros."));

        await ExecutarVersaoNovaComFalhaAsync(montagem, nova);
    }

    [Fact(DisplayName = "Falha depois da mesclagem dos logradouros desfaz a carga e mantém a versão anterior publicada")]
    public async Task FalhaDepoisDaMesclagemDosLogradouros_DesfazTudo()
    {
        using MontagemDaCarga montagem = new(_fixture)
        {
            DecorarFolhas = folhas => new FolhasComGancho(
                folhas, () => Task.FromException(new InvalidOperationException("Falha simulada depois da mesclagem."))),
        };

        await ExecutarVersaoNovaComFalhaAsync(montagem, VersoesFicticias.Nova());
    }

    [Fact(DisplayName = "Falha depois da marcação de registros não vigentes desfaz a carga e mantém a versão anterior publicada")]
    public async Task FalhaDepoisDaMarcacaoDeVigencia_DesfazTudo()
    {
        int naoVigentesNaTransacao = 0;
        RelogioFixo fixo = new();
        await using GeoDbContext contexto = _fixture.CreateDbContext(fixo);
        RelogioDaConclusaoComFalha relogio = new(fixo, () => naoVigentesNaTransacao = ContarNaoVigentes(contexto));
        using MontagemDaCarga montagem = MontarComFalhaNaConclusao(relogio);
        Guid id = await PrepararVersaoNovaAsync(montagem);

        await montagem.ExecutarAsync(id, new FabricaDeFonteFixa(VersoesFicticias.Nova()), contexto);

        naoVigentesNaTransacao.Should().BeGreaterThan(0, "a marcação já tinha alterado linhas dentro da transação quando a falha ocorreu");
        await ConferirVersaoNovaDesfeitaAsync(montagem, id);
    }

    private MontagemDaCarga MontarComFalhaNaConclusao(RelogioDaConclusaoComFalha relogio) =>
        new(_fixture)
        {
            RelogioDaCarga = relogio,
            DecorarFolhas = folhas => new FolhasComGancho(folhas, () => FalharNaConclusao(relogio)),
        };

    // A primeira chamada do relógio depois das folhas carimba a marcação de não vigentes; a
    // seguinte carimba a conclusão da execução, já com a marcação feita na transação.
    private static Task FalharNaConclusao(RelogioDaConclusaoComFalha relogio)
    {
        relogio.FalharAposChamadas(1);
        return Task.CompletedTask;
    }

    private static int ContarNaoVigentes(GeoDbContext contexto) =>
        contexto.Cidades.Count(c => !c.Vigente) + contexto.Logradouros.Count(l => !l.Vigente);

    private async Task ExecutarVersaoNovaComFalhaAsync(MontagemDaCarga montagem, FonteEmMemoria nova)
    {
        Guid id = await PrepararVersaoNovaAsync(montagem);

        await montagem.ExecutarAsync(id, new FabricaDeFonteFixa(nova));

        await ConferirVersaoNovaDesfeitaAsync(montagem, id);
    }

    private async Task<Guid> PrepararVersaoNovaAsync(MontagemDaCarga montagem)
    {
        await _banco.LimparAsync();
        using MontagemDaCarga anterior = new(_fixture);
        await anterior.PublicarAsync(VersoesFicticias.Anterior());
        return await montagem.RegistrarExecucaoAsync(VersoesFicticias.VersaoNova);
    }

    private async Task ConferirVersaoNovaDesfeitaAsync(MontagemDaCarga montagem, Guid id)
    {
        GeoImportacaoExecucao execucao = await _banco.ExecucaoAsync(id);
        execucao.Status.Should().Be(StatusImportacao.Falhou);
        execucao.ConcluidoEm.Should().NotBeNull();
        await _banco.ConferirSomenteVersaoAnteriorAsync();
        await montagem.Cache.DidNotReceiveWithAnyArgs().InvalidarAsync(default!, default);
    }
}
