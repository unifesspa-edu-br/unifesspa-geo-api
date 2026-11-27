namespace Unifesspa.Geo.IntegrationTests.Etl.Pacote;

using AwesomeAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Unifesspa.Geo.Infrastructure.Persistence.Etl;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Na partida, antes de aceitar envios: o diretório de pacotes é limpo e os esquemas de carga
/// órfãos são descartados quando a trava consultiva está livre; com a trava ocupada por outra
/// instância, só o diretório (que é do próprio processo) é limpo.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class LimpezaDaPartidaDaCargaTests : IDisposable
{
    private readonly GeoPostgisFixture _fixture;
    private readonly ConexaoDoBancoDaCarga _conexao;
    private readonly string _diretorio = Path.Combine(Path.GetTempPath(), $"pacote-dne-{Guid.NewGuid():N}");
    private readonly ArquivoDoPacoteDne _arquivos;

    public LimpezaDaPartidaDaCargaTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _conexao = new ConexaoDoBancoDaCarga(fixture);
        _arquivos = new ArquivoDoPacoteDne(_diretorio);
    }

    public void Dispose()
    {
        if (Directory.Exists(_diretorio))
        {
            Directory.Delete(_diretorio, recursive: true);
        }
    }

    [Fact(DisplayName = "Com a trava livre, a partida limpa o diretório de pacotes e descarta o esquema de carga órfão")]
    public async Task TravaLivre_LimpaDiretorioEDescartaEsquemaOrfao()
    {
        string orfao = await CriarResiduosAsync();

        await CriarServico().StartAsync(CancellationToken.None);

        Directory.EnumerateFileSystemEntries(_diretorio).Should().BeEmpty();
        (await _conexao.ExisteEsquemaAsync(orfao)).Should().BeFalse();
    }

    [Fact(DisplayName = "Com a trava ocupada por outra instância, a partida limpa o diretório mas não toca no esquema de carga")]
    public async Task TravaOcupada_LimpaDiretorioEPreservaEsquema()
    {
        string emUso = await CriarResiduosAsync();
        try
        {
            await IniciarComATravaDeOutraInstanciaAsync();

            Directory.EnumerateFileSystemEntries(_diretorio).Should().BeEmpty();
            (await _conexao.ExisteEsquemaAsync(emUso)).Should().BeTrue("a carga de outra instância pode estar usando o esquema");
        }
        finally
        {
            await _conexao.ExecutarAsync($"DROP SCHEMA IF EXISTS \"{emUso}\" CASCADE");
        }
    }

    private async Task IniciarComATravaDeOutraInstanciaAsync()
    {
        TravaConsultivaDaCargaDne outraInstancia = new(_conexao.Configuracao);
        IAsyncDisposable? trava = await outraInstancia.TentarAdquirirAsync(CancellationToken.None);
        trava.Should().NotBeNull();
        await using (trava!)
        {
            await CriarServico().StartAsync(CancellationToken.None);
        }
    }

    private async Task<string> CriarResiduosAsync()
    {
        Guid execucao = Guid.NewGuid();
        string esquema = ComandosDoEsquemaDeCarga.Nome(execucao);
        await _conexao.ExecutarAsync($"CREATE SCHEMA \"{esquema}\"");
        Directory.CreateDirectory(_diretorio);
        await File.WriteAllTextAsync(_arquivos.CaminhoDe(execucao), "resíduo");
        return esquema;
    }

    private GeoReconciliacaoStartupHostedService CriarServico()
    {
        EsquemaDeCargaDne esquemas = new(_conexao.Configuracao, _arquivos, NullLogger<EsquemaDeCargaDne>.Instance);
        return new GeoReconciliacaoStartupHostedService(
            ServicosDaPartidaParaTeste.Criar(_fixture, esquemas),
            new TravaConsultivaDaCargaDne(_conexao.Configuracao),
            _arquivos,
            NullLogger<GeoReconciliacaoStartupHostedService>.Instance);
    }
}
