namespace Unifesspa.Geo.IntegrationTests.Etl;

using AwesomeAssertions;

using Microsoft.EntityFrameworkCore;

using NSubstitute;

using Unifesspa.Geo.Application.Abstractions;
using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Observability;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.Infrastructure.Persistence.Etl;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Cada forma de a carga terminar antes do fim grava a mensagem própria na execução, desfaz a
/// publicação, apaga o arquivo do pacote e só devolve a exceção quando foi o desligamento.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class DesfechosDaCargaInterrompidaTests
{
    private const string Versao = "202601";

    private readonly GeoPostgisFixture _fixture;

    public DesfechosDaCargaInterrompidaTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(DisplayName = "Carga que excede a duração máxima grava a interrupção por prazo, não propaga e apaga o pacote")]
    public async Task PrazoExcedido_GravaAMensagemDoPrazo_NaoPropagaEApagaOPacote()
    {
        CenarioDaCargaPeriodica cenario = new(_fixture) { Opcoes = new EtlOpcoes { DuracaoMaximaDaCarga = TimeSpan.FromMilliseconds(500) } };
        FonteDaExecucaoComDesfecho fonte = new(EsperarOCancelamentoAsync);

        Guid id = await ExecutarAsync(cenario, fonte, CancellationToken.None);

        await ConferirFalhaAsync(cenario, id, "Carga interrompida ao exceder a duração máxima configurada.");
        fonte.PacotesApagados.Should().Be(1);
    }

    [Fact(DisplayName = "Desligamento durante a carga grava a interrupção por cancelamento, propaga o cancelamento e apaga o pacote")]
    public async Task Desligamento_GravaAMensagemDoCancelamento_PropagaEApagaOPacote()
    {
        CenarioDaCargaPeriodica cenario = new(_fixture);
        FonteDaExecucaoComDesfecho fonte = new(EsperarOCancelamentoAsync);
        using CancellationTokenSource desligamento = new(TimeSpan.FromMilliseconds(500));

        Func<Task> carga = () => ExecutarAsync(cenario, fonte, desligamento.Token);

        await carga.Should().ThrowAsync<OperationCanceledException>();
        await ConferirFalhaAsync(cenario, await UnicaExecucaoAsync(cenario), "Carga interrompida (cancelamento/desligamento).");
        fonte.PacotesApagados.Should().Be(1);
    }

    [Fact(DisplayName = "Falha inesperada na carga grava a mensagem geral, não propaga e apaga o pacote")]
    public async Task FalhaGeral_GravaAMensagemGeral_NaoPropagaEApagaOPacote()
    {
        CenarioDaCargaPeriodica cenario = new(_fixture);
        FonteDaExecucaoComDesfecho fonte = new(_ => throw new InvalidOperationException("falha simulada"));

        Guid id = await ExecutarAsync(cenario, fonte, CancellationToken.None);

        await ConferirFalhaAsync(cenario, id, "Falha na carga da DNE.");
        fonte.PacotesApagados.Should().Be(1);
    }

    [Fact(DisplayName = "Recusa do pacote grava a mensagem com a tabela e a linha, não propaga e apaga o pacote")]
    public async Task RecusaDoPacote_GravaATabelaEALinha_NaoPropagaEApagaOPacote()
    {
        CenarioDaCargaPeriodica cenario = new(_fixture);
        FonteDaExecucaoComDesfecho fonte = new(_ => throw new ErroDeLeituraDoPacoteDneException("logradouro", 12, "valor fora do formato"));

        Guid id = await ExecutarAsync(cenario, fonte, CancellationToken.None);

        await ConferirFalhaAsync(cenario, id, "Pacote recusado: Tabela logradouro, linha 12: valor fora do formato");
        fonte.PacotesApagados.Should().Be(1);
    }

    private static async Task<IFonteDaCargaDne> EsperarOCancelamentoAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        throw new InvalidOperationException("A espera só termina pelo cancelamento.");
    }

    private static async Task<Guid> ExecutarAsync(CenarioDaCargaPeriodica cenario, IFonteDaExecucaoDne fonte, CancellationToken cancellationToken)
    {
        await cenario.LimparAsync();
        Guid id = await cenario.CriarExecucaoAsync(Versao);
        await using GeoDbContext contexto = cenario.CriarContexto();
        using GeoEtlMetrics metricas = new();
        Lazy<IGeoCepCacheInvalidador> cache = new(() => Substitute.For<IGeoCepCacheInvalidador>());
        CargaDaImportacao executor = cenario.CriarExecutor(contexto, fonte, cache, metricas);

        await executor.ExecutarAsync(await cenario.ReservarAsync(id), cancellationToken);

        return id;
    }

    private static async Task<Guid> UnicaExecucaoAsync(CenarioDaCargaPeriodica cenario)
    {
        await using GeoDbContext leitura = cenario.CriarContexto();
        return await leitura.ImportacaoExecucoes.Select(e => e.Id).SingleAsync();
    }

    private static async Task ConferirFalhaAsync(CenarioDaCargaPeriodica cenario, Guid id, string mensagem)
    {
        GeoImportacaoExecucao execucao = await cenario.LerExecucaoAsync(id);
        execucao.Status.Should().Be(StatusImportacao.Falhou);
        execucao.Mensagem.Should().Be(mensagem);
        execucao.ConcluidoEm.Should().NotBeNull();
    }

    private sealed class FonteDaExecucaoComDesfecho : IFonteDaExecucaoDne
    {
        private readonly Func<CancellationToken, Task<IFonteDaCargaDne>> _preparar;

        public FonteDaExecucaoComDesfecho(Func<CancellationToken, Task<IFonteDaCargaDne>> preparar)
        {
            _preparar = preparar;
        }

        public int PacotesApagados { get; private set; }

        public Task<IFonteDaCargaDne> PrepararAsync(Guid execucaoId, string versao, CancellationToken cancellationToken) =>
            _preparar(cancellationToken);

        public Task DescartarPacoteAsync(Guid execucaoId)
        {
            PacotesApagados++;
            return Task.CompletedTask;
        }

        public Task<int> DescartarSobrasAsync(CancellationToken cancellationToken) => Task.FromResult(0);
    }
}
