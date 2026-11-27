namespace Unifesspa.Geo.IntegrationTests.Etl;

using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using Unifesspa.Geo.Application.DTOs;
using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Domain.Errors;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.Infrastructure.Persistence.Etl;
using Unifesspa.Geo.IntegrationTests.Infrastructure;
using Unifesspa.Geo.Kernel.Results;

/// <summary>
/// A guarda de versão progressiva só considera as execuções concluídas e os dados publicados:
/// uma execução que falhou, mesmo de versão futura, não eleva o piso. Toda recusa apaga o arquivo.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class GuardaDeVersaoDoRegistroTests : IDisposable
{
    private static readonly DateTimeOffset Instante = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly GeoPostgisFixture _fixture;
    private readonly BancoDaCarga _banco;
    private readonly EnvioDoPacoteParaTeste _envio = new();

    public GuardaDeVersaoDoRegistroTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _banco = new BancoDaCarga(fixture);
    }

    public void Dispose() => _envio.Dispose();

    [Fact(DisplayName = "Execução falha de versão futura (202612) não bloqueia a versão intermediária válida (202611)")]
    public async Task ExecucaoFalhaDeVersaoFutura_NaoBloqueiaVersaoIntermediaria()
    {
        await PrepararHistoricoAsync();

        Result<EnvioDoPacoteRegistrado> resultado = await EnviarAsync("202611");

        resultado.IsSuccess.Should().BeTrue("a falha de 202612 não conta como versão aplicada");
    }

    [Fact(DisplayName = "Versão anterior à última concluída (202610) é recusada como não progressiva e o arquivo é apagado")]
    public async Task VersaoAnteriorAUltimaConcluida_ERecusada_EOArquivoESomem()
    {
        await PrepararHistoricoAsync();

        Result<EnvioDoPacoteRegistrado> resultado = await EnviarAsync("202609");

        resultado.IsFailure.Should().BeTrue();
        resultado.Error!.Code.Should().Be(GeoImportacaoErrorCodes.VersaoNaoProgressiva);
        _envio.NomesDosArquivos().Should().BeEmpty();
    }

    [Fact(DisplayName = "Versão anterior aos dados publicados, sem histórico de execução, é recusada como não progressiva")]
    public async Task VersaoAnteriorAosDadosPublicados_SemHistorico_ERecusada()
    {
        await _banco.LimparAsync();
        await SemearCidadeAsync("202602");

        Result<EnvioDoPacoteRegistrado> resultado = await EnviarAsync("202601");

        resultado.Error!.Code.Should().Be(GeoImportacaoErrorCodes.VersaoNaoProgressiva);
        _envio.NomesDosArquivos().Should().BeEmpty();
    }

    private Task<Result<EnvioDoPacoteRegistrado>> EnviarAsync(string versao) =>
        _envio.EnviarAsync(_fixture, _envio.Receber(versao));

    private async Task SemearCidadeAsync(string versao)
    {
        await using GeoDbContext contexto = _fixture.CreateDbContext();
        await using FonteEmMemoria fonte = new() { Versao = versao };
        fonte.Paises.Add(DadosDne.Pais("BRA", "Brasil", "BR"));
        fonte.Estados.Add(DadosDne.Estado("PA", "Pará"));
        fonte.EstadoIndicadores.Add(DadosDne.EstadoIndicador("PA", "15"));
        fonte.Cidades.Add(DadosDne.Cidade("1500402", "Marabá", "PA"));

        GeoImportadorPaisEstadoCidade importador = new(contexto, NullLogger<GeoImportadorPaisEstadoCidade>.Instance);
        await importador.ImportarAsync(fonte, CancellationToken.None);
    }

    private async Task PrepararHistoricoAsync()
    {
        await _banco.LimparAsync();
        await using GeoDbContext contexto = _fixture.CreateDbContext();
        GeoImportacaoExecucao concluida = GeoImportacaoExecucao.Iniciar(PacotesDeTeste.Recebido("202610"), "teste", Instante).Value!;
        concluida.Concluir(Instante.AddMinutes(5), "{}", "concluída");
        GeoImportacaoExecucao falha = GeoImportacaoExecucao.Iniciar(PacotesDeTeste.Recebido("202612"), "teste", Instante.AddDays(1)).Value!;
        falha.Falhar(Instante.AddDays(1).AddMinutes(5), "falha simulada");
        contexto.ImportacaoExecucoes.AddRange(concluida, falha);
        await contexto.SaveChangesAsync();
    }
}
