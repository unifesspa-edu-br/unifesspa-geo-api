namespace Unifesspa.Geo.IntegrationTests.Etl;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.Infrastructure.Persistence.Etl;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Bulk;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Apoio das classes de teste da carga das localidades: pré-carrega o topo (país, estado e
/// cidade), executa a carga das folhas numa transação e traz as versões fictícias usadas pelos
/// cenários. Usado por composição.
/// </summary>
internal sealed class CenarioDasLocalidades
{
    public const string CodigoMaraba = "1500402";
    public const string CodigoSaoPaulo = "3550308";
    public const int IdCidadeMaraba = 1;
    public const int IdCidadeSaoPaulo = 2;
    public const int IdCidadeInexistente = 999;

    private readonly GeoPostgisFixture _fixture;
    private readonly RelogioFixo _relogio = new();

    public CenarioDasLocalidades(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task PrepararTopoAsync(IGeoFonteDados fonte)
    {
        await LimparAsync();
        await using GeoDbContext ctx = _fixture.CreateDbContext();
        GeoImportadorPaisEstadoCidade importador = new(ctx, NullLogger<GeoImportadorPaisEstadoCidade>.Instance);
        await importador.ImportarAsync(fonte, CancellationToken.None);
    }

    public async Task<RelatorioImportacao> ExecutarLocalidadesAsync(IGeoFonteDados fonte)
    {
        await using GeoDbContext ctx = _fixture.CreateDbContext();
        await using IDbContextTransaction transacao = await ctx.Database.BeginTransactionAsync();
        IConfiguration configuracao = new ConfigurationBuilder().Build();
        GeoImportadorDistritoBairro distritoBairro = new(ctx);
        ImportadorDeLogradourosPorCopia logradouro = new(ctx, configuracao, _relogio, NullLogger<ImportadorDeLogradourosPorCopia>.Instance);
        GeoImportadorLocalidades importador = new(distritoBairro, logradouro, NullLogger<GeoImportadorLocalidades>.Instance);
        RelatorioImportacao relatorio = await importador.ImportarAsync(fonte, CancellationToken.None);
        await transacao.CommitAsync();
        return relatorio;
    }

    public async Task LimparAsync()
    {
        await using GeoDbContext ctx = _fixture.CreateDbContext();
        // pais CASCADE limpa a cadeia de FK (estado→cidade→distrito/bairro→logradouro);
        // complemento e grande usuário não têm FK para essa cadeia, então são truncados
        // à parte para isolar cada teste no banco compartilhado da coleção.
        await ctx.Database.ExecuteSqlRawAsync("TRUNCATE TABLE pais, logradouro_complemento, cep_grande_usuario CASCADE");
    }

    public static FonteEmMemoria FonteCompleta()
    {
        FonteEmMemoria fonte = new() { Versao = "202601" };

        // Topo (carregado pelo importador da #672 antes das folhas).
        fonte.Paises.Add(DadosDne.Pais("BRA", "Brasil", "BR"));
        fonte.Estados.Add(DadosDne.Estado("PA", "Pará", capital: "Belém", faixaIni: "66000000", faixaFim: "68899999"));
        fonte.Estados.Add(DadosDne.Estado("SP", "São Paulo", regiao: "Sudeste", capital: "São Paulo", faixaIni: "01000000", faixaFim: "19999999"));
        fonte.EstadoIndicadores.Add(DadosDne.EstadoIndicador("PA", "15"));
        fonte.EstadoIndicadores.Add(DadosDne.EstadoIndicador("SP", "35"));
        fonte.Cidades.Add(DadosDne.Cidade(CodigoMaraba, "Marabá", "PA", ddd: "94"));
        fonte.Cidades.Add(DadosDne.Cidade(CodigoSaoPaulo, "São Paulo", "SP", ddd: "11"));

        // Mapa id_cidade (int4 da fonte) → código IBGE, para resolver a FK das folhas.
        fonte.CidadeIds.Add(DadosDne.CidadeId(IdCidadeMaraba, CodigoMaraba));
        fonte.CidadeIds.Add(DadosDne.CidadeId(IdCidadeSaoPaulo, CodigoSaoPaulo));

        // Folhas.
        fonte.Distritos.Add(DadosDne.Distrito(10, "Cidade Nova", IdCidadeMaraba, latitude: "-5.36", longitude: "-49.11"));
        fonte.Distritos.Add(DadosDne.Distrito(20, "Centro", IdCidadeSaoPaulo));
        fonte.DistritoFaixas.Add(DadosDne.Faixa(10, "68500000", "68519999"));

        fonte.Bairros.Add(DadosDne.Bairro(100, "Cidade Nova", IdCidadeMaraba));
        fonte.Bairros.Add(DadosDne.Bairro(200, "Sé", IdCidadeSaoPaulo));
        fonte.BairroFaixas.Add(DadosDne.Faixa(100, "68500000", "68509999"));

        fonte.CepGrandesUsuarios.Add(DadosDne.GrandeUsuario("68500900", "Prefeitura de Marabá"));

        fonte.LogradouroComplementos.Add(DadosDne.Complemento("68500000", "lado par"));
        fonte.LogradouroComplementos.Add(DadosDne.Complemento("68500000", "lado ímpar"));

        fonte.Logradouros.Add(DadosDne.Logradouro("68500000", "Rua A", IdCidadeMaraba, tipo: "Rua", bairroIdDne: 100, distritoIdDne: 10, latitude: "-5.36867", longitude: "-49.11731", cepAtivo: "S"));
        fonte.Logradouros.Add(DadosDne.Logradouro("68500001", "Rua B", IdCidadeMaraba, distritoIdDne: null, cepAtivo: "N"));

        return fonte;
    }

    public static FonteEmMemoria FonteComCepGeral()
    {
        // FonteCompleta já traz o distrito 10 (Marabá) e logradouros reais.
        FonteEmMemoria fonte = FonteCompleta();
        fonte.CepGerais.Add(DadosDne.CepGeral("68525000", IdCidadeMaraba, latitude: "-5.046", longitude: "-48.59947"));
        fonte.CepGerais.Add(DadosDne.CepGeral("68525100", IdCidadeMaraba, distritoIdDne: 10));
        return fonte;
    }
}
