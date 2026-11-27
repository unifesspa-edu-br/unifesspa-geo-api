namespace Unifesspa.Geo.IntegrationTests.Etl.Pacote;

using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Esquema de carga em PostgreSQL real: criado e populado por COPY binário a partir de um pacote
/// sintético, descartado junto com a fonte ao fim e em falha, e limpo quando sobra de uma carga interrompida.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class EsquemaDeCargaDneTests : IDisposable
{
    private const string Versao = "202602";

    private readonly string _diretorio = Path.Combine(Path.GetTempPath(), $"pacote-dne-{Guid.NewGuid():N}");
    private readonly ConexaoDoBancoDaCarga _banco;
    private readonly ArquivoDoPacoteDne _arquivos;
    private readonly EsquemaDeCargaDne _esquemaDeCarga;

    public EsquemaDeCargaDneTests(GeoPostgisFixture fixture)
    {
        _banco = new ConexaoDoBancoDaCarga(fixture);
        _arquivos = new ArquivoDoPacoteDne(_diretorio);
        _esquemaDeCarga = new EsquemaDeCargaDne(_banco.Configuracao, _arquivos, NullLogger<EsquemaDeCargaDne>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_diretorio))
        {
            Directory.Delete(_diretorio, recursive: true);
        }
    }

    [Fact(DisplayName = "Preparar cria as 15 tabelas UNLOGGED, carrega por COPY e apaga o arquivo; descartar a fonte apaga o esquema")]
    public async Task Preparar_CriaECarregaEDescartarApagaOEsquema()
    {
        Guid execucao = Guid.NewGuid();
        string esquema = ComandosDoEsquemaDeCarga.Nome(execucao);
        PacoteSinteticoDne.Completo(Versao).Gravar(_diretorio, execucao);

        await using (IFonteDaCargaDne fonte = await _esquemaDeCarga.PrepararAsync(execucao, Versao, CancellationToken.None))
        {
            (await _banco.TabelasSemRegistroDeTransacaoAsync(esquema)).Should().Be(15);
            (await _banco.LinhasAsync(esquema, "cidade")).Should().Be(1);
            (await _banco.LinhasAsync(esquema, "logradouro")).Should().Be(2);
            (await _banco.LinhasAsync(esquema, "bairro")).Should().Be(0);
            File.Exists(_arquivos.CaminhoDe(execucao)).Should().BeFalse("o arquivo é apagado ao terminar de popular o esquema");
            fonte.Versao.Should().Be(Versao);
        }

        (await _banco.ExisteEsquemaAsync(esquema)).Should().BeFalse();
    }

    [Fact(DisplayName = "A fonte lê do esquema de carga os valores do pacote")]
    public async Task Fonte_LeOsValoresDoPacote()
    {
        Guid execucao = Guid.NewGuid();
        PacoteSinteticoDne.Completo(Versao).Gravar(_diretorio, execucao);

        await using IFonteDaCargaDne fonte = await _esquemaDeCarga.PrepararAsync(execucao, Versao, CancellationToken.None);

        List<CidadeIdCru> cidades = [];
        await foreach (CidadeIdCru cidade in fonte.LerCidadeIdsAsync(CancellationToken.None))
        {
            cidades.Add(cidade);
        }

        cidades.Should().ContainSingle().Which.Should().Match<CidadeIdCru>(c => c.IdCidade == 1 && c.CodigoIbge == "1500402");
    }

    [Fact(DisplayName = "Coluna extra do arquivo não vira coluna do esquema de carga")]
    public async Task ColunaExtra_NaoChegaAoEsquema()
    {
        Guid execucao = Guid.NewGuid();
        string esquema = ComandosDoEsquemaDeCarga.Nome(execucao);
        PacoteSinteticoDne.Completo(Versao).ComColunaExtra("cidade", "coluna_extra_ficticia").Gravar(_diretorio, execucao);

        await using IFonteDaCargaDne fonte = await _esquemaDeCarga.PrepararAsync(execucao, Versao, CancellationToken.None);

        (await _banco.ColunasComNomeAsync(esquema, "coluna_extra_ficticia")).Should().Be(0);
        (await _banco.LinhasAsync(esquema, "cidade")).Should().Be(1);
    }

    [Fact(DisplayName = "Descartar o pacote apaga só o arquivo da execução e aceita arquivo ausente")]
    public async Task DescartarPacote_ApagaOArquivo()
    {
        Guid execucao = Guid.NewGuid();
        PacoteSinteticoDne.Completo(Versao).Gravar(_diretorio, execucao);

        await _esquemaDeCarga.DescartarPacoteAsync(execucao);
        await _esquemaDeCarga.DescartarPacoteAsync(execucao);

        File.Exists(_arquivos.CaminhoDe(execucao)).Should().BeFalse();
    }

    [Fact(DisplayName = "Linha fora da gramática interrompe a preparação com tabela e linha e não deixa esquema nem arquivo")]
    public async Task LinhaInvalida_InterrompeESemSobras()
    {
        Guid execucao = Guid.NewGuid();
        string esquema = ComandosDoEsquemaDeCarga.Nome(execucao);
        PacoteSinteticoDne.Completo(Versao).ComLinhaBruta("logradouro", "'x', E'y'").Gravar(_diretorio, execucao);

        Func<Task> preparar = () => _esquemaDeCarga.PrepararAsync(execucao, Versao, CancellationToken.None);

        (await preparar.Should().ThrowAsync<ErroDeLeituraDoPacoteDneException>())
            .Which.Tabela.Should().Be("logradouro");
        (await _banco.ExisteEsquemaAsync(esquema)).Should().BeFalse();
        File.Exists(_arquivos.CaminhoDe(execucao)).Should().BeFalse();
    }

    [Fact(DisplayName = "Texto com aspas e comando SQL no valor é gravado como dado e não é executado")]
    public async Task TextoComComandoSql_EhGravadoComoDado()
    {
        Guid execucao = Guid.NewGuid();
        string esquema = ComandosDoEsquemaDeCarga.Nome(execucao);
        PacoteSinteticoDne.Completo(Versao)
            .Com("log_complemento", ("cep", "68500002"), ("complemento", "x'); DROP TABLE cidade; --"))
            .Gravar(_diretorio, execucao);

        await using IFonteDaCargaDne fonte = await _esquemaDeCarga.PrepararAsync(execucao, Versao, CancellationToken.None);

        (await _banco.LinhasAsync(esquema, "log_complemento")).Should().Be(2);
        (await _banco.LinhasAsync("public", "cidade")).Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact(DisplayName = "Descartar sobras remove só os esquemas com o nome exato de uma carga e conta quantos removeu")]
    public async Task DescartarSobras_RemoveSoOsEsquemasDeCarga()
    {
        (string sobra1, string sobra2) = (ComandosDoEsquemaDeCarga.Nome(Guid.NewGuid()), ComandosDoEsquemaDeCarga.Nome(Guid.NewGuid()));
        await CriarEsquemasAsync(sobra1, sobra2, "carga_dne_de_outro_uso");

        try
        {
            int removidos = await _esquemaDeCarga.DescartarSobrasAsync(CancellationToken.None);

            removidos.Should().BeGreaterThanOrEqualTo(2);
            await ConferirEsquemasRemovidosAsync(sobra1, sobra2);
            (await _banco.ExisteEsquemaAsync("carga_dne_de_outro_uso")).Should().BeTrue("o nome não tem a forma de um esquema de carga");
        }
        finally
        {
            await _banco.ExecutarAsync("DROP SCHEMA IF EXISTS carga_dne_de_outro_uso CASCADE");
        }
    }

    private async Task CriarEsquemasAsync(params string[] nomes)
    {
        foreach (string nome in nomes)
        {
            await _banco.ExecutarAsync($"CREATE SCHEMA IF NOT EXISTS \"{nome}\"");
        }
    }

    private async Task ConferirEsquemasRemovidosAsync(params string[] nomes)
    {
        foreach (string nome in nomes)
        {
            (await _banco.ExisteEsquemaAsync(nome)).Should().BeFalse();
        }
    }
}
