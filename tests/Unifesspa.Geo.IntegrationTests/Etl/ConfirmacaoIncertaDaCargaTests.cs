namespace Unifesspa.Geo.IntegrationTests.Etl;

using AwesomeAssertions;

using Microsoft.EntityFrameworkCore.Diagnostics;

using NSubstitute;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Quando a confirmação da transação falha, a carga consulta o banco numa conexão nova: se a
/// execução já consta concluída, o sucesso é preservado; se ainda consta em andamento, a
/// transação foi desfeita e a execução passa a falha.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class ConfirmacaoIncertaDaCargaTests
{
    private readonly GeoPostgisFixture _fixture;
    private readonly BancoDaCarga _banco;

    public ConfirmacaoIncertaDaCargaTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _banco = new BancoDaCarga(fixture);
    }

    [Fact(DisplayName = "Falha na resposta de uma confirmação que já valeu preserva a execução concluída e sela o cache")]
    public async Task ConfirmacaoEfetivada_PreservaConcluida()
    {
        await _banco.LimparAsync();
        using MontagemDaCarga montagem = new(_fixture);

        Guid id = await ExecutarComConfirmacaoAsync(montagem, new ConfirmacaoEfetivadaComFalhaNaResposta());

        GeoImportacaoExecucao execucao = await _banco.ExecucaoAsync(id);
        execucao.Status.Should().Be(StatusImportacao.Concluida);
        await montagem.Cache.Received(1).InvalidarAsync($"{VersoesFicticias.VersaoAnterior}.{id:N}", Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Falha na confirmação desfeita pelo banco marca a execução como falha, sem selar o cache nem publicar dados")]
    public async Task ConfirmacaoDesfeita_MarcaFalhou()
    {
        await _banco.LimparAsync();
        using MontagemDaCarga montagem = new(_fixture);

        Guid id = await ExecutarComConfirmacaoAsync(montagem, new ConfirmacaoDesfeitaPeloBanco());

        GeoImportacaoExecucao execucao = await _banco.ExecucaoAsync(id);
        execucao.Status.Should().Be(StatusImportacao.Falhou);
        execucao.Mensagem.Should().Contain("confirmação da transação");
        await montagem.Cache.DidNotReceiveWithAnyArgs().InvalidarAsync(default!, default);
        await using GeoDbContext leitura = _fixture.CreateDbContext();
        leitura.Cidades.Should().BeEmpty("a transação desfeita não publicou nenhuma cidade");
    }

    private async Task<Guid> ExecutarComConfirmacaoAsync(MontagemDaCarga montagem, IInterceptor interceptador)
    {
        Guid id = await montagem.RegistrarExecucaoAsync(VersoesFicticias.VersaoAnterior);
        await using GeoDbContext contexto = _fixture.CreateDbContext([interceptador]);

        await montagem.ExecutarAsync(id, new FabricaDeFonteFixa(VersoesFicticias.Anterior()), contexto);

        return id;
    }
}
