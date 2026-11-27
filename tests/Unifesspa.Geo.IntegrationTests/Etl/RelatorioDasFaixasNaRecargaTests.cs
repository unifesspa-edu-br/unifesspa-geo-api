namespace Unifesspa.Geo.IntegrationTests.Etl;

using AwesomeAssertions;

using Microsoft.EntityFrameworkCore;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.Infrastructure.Persistence.Etl;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Fixa os números do relatório das faixas de CEP do estado e da cidade na primeira carga e na
/// recarga, e que a faixa de um estado ou de uma cidade que não vem na recarga fica como estava.
/// A ordem dos números é: lidos, inseridos, atualizados, repetidos na fonte, sem chave, sem pai.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class RelatorioDasFaixasNaRecargaTests
{
    private const string CodigoDeMaraba = "1500402";
    private const string CodigoDeSaoPaulo = "3550308";

    private readonly GeoPostgisFixture _fixture;

    public RelatorioDasFaixasNaRecargaTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(DisplayName = "Primeira carga: as faixas do estado contam inseridas, repetida na fonte, sem CEP e sem estado carregado")]
    public async Task PrimeiraCarga_RelatorioDasFaixasDoEstado()
    {
        await LimparAsync();

        RelatorioImportacao relatorio = await CargaPelaEtl.ExecutarAsync(_fixture, FonteDasFaixasDoEstado("202601"));

        Numeros(relatorio, "estado_faixa").Should().Be((6, 3, 0, 1, 1, 1));
    }

    [Fact(DisplayName = "Recarga: as faixas do estado contam atualizadas, nova inserida, repetida na fonte e estado fora da carga")]
    public async Task Recarga_RelatorioDasFaixasDoEstado()
    {
        await LimparAsync();
        await CargaPelaEtl.ExecutarAsync(_fixture, FonteDasFaixasDoEstado("202601"));

        RelatorioImportacao relatorio = await CargaPelaEtl.ExecutarAsync(_fixture, RecargaDasFaixasDoEstado("202602"));

        Numeros(relatorio, "estado_faixa").Should().Be((5, 1, 2, 1, 0, 1));
    }

    [Fact(DisplayName = "Primeira carga: as faixas da cidade contam inseridas, repetida na fonte, sem CEP e sem cidade carregada")]
    public async Task PrimeiraCarga_RelatorioDasFaixasDaCidade()
    {
        await LimparAsync();

        RelatorioImportacao relatorio = await CargaPelaEtl.ExecutarAsync(_fixture, FonteDasFaixasDaCidade("202601"));

        Numeros(relatorio, "cidade_faixa").Should().Be((5, 2, 0, 1, 1, 1));
    }

    [Fact(DisplayName = "Recarga: as faixas da cidade contam atualizada, nova inserida, repetida na fonte e cidade fora da carga")]
    public async Task Recarga_RelatorioDasFaixasDaCidade()
    {
        await LimparAsync();
        await CargaPelaEtl.ExecutarAsync(_fixture, FonteDasFaixasDaCidade("202601"));

        RelatorioImportacao relatorio = await CargaPelaEtl.ExecutarAsync(_fixture, RecargaDasFaixasDaCidade("202602"));

        Numeros(relatorio, "cidade_faixa").Should().Be((4, 1, 1, 1, 0, 1));
    }

    [Fact(DisplayName = "Recarga sem um estado: a faixa dele fica com a versão e a descrição da carga anterior")]
    public async Task Recarga_FaixaDoEstadoForaDaCarga_FicaIntocada()
    {
        await LimparAsync();
        await CargaPelaEtl.ExecutarAsync(_fixture, FonteDasFaixasDoEstado("202601"));

        await CargaPelaEtl.ExecutarAsync(_fixture, RecargaDasFaixasDoEstado("202602"));

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        EstadoFaixaCep faixa = await leitura.EstadoFaixasCep.SingleAsync(f => f.CepInicial == "01000000");
        (faixa.VersaoDataset, faixa.Descricao).Should().Be(("202601", "Capital paulista"));
    }

    [Fact(DisplayName = "Recarga sem uma cidade: a faixa dela fica com a versão da carga anterior")]
    public async Task Recarga_FaixaDaCidadeForaDaCarga_FicaIntocada()
    {
        await LimparAsync();
        await CargaPelaEtl.ExecutarAsync(_fixture, FonteDasFaixasDaCidade("202601"));

        await CargaPelaEtl.ExecutarAsync(_fixture, RecargaDasFaixasDaCidade("202602"));

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        CidadeFaixaCep faixa = await leitura.CidadeFaixasCep.SingleAsync(f => f.CepInicial == "01000000");
        faixa.VersaoDataset.Should().Be("202601");
    }

    private static (int Lidos, int Inseridos, int Atualizados, int Repetidos, int SemChave, int SemPai) Numeros(
        RelatorioImportacao relatorio,
        string tabela)
    {
        ContadorTabela c = relatorio.Tabelas[tabela];
        return (c.Lidos, c.Inseridos, c.Atualizados, c.Duplicados, c.IgnoradosSemChave, c.Orfaos);
    }

    private static FonteEmMemoria FonteDasFaixasDoEstado(string versao)
    {
        FonteEmMemoria fonte = BaseComMaraba(versao);
        AdicionarSaoPaulo(fonte);
        fonte.EstadoFaixas.Add(DadosDne.EstadoFaixa("PA", "66000000", "66999999", "Capital"));
        fonte.EstadoFaixas.Add(DadosDne.EstadoFaixa("PA", "67000000", "68899999", "Interior"));
        fonte.EstadoFaixas.Add(DadosDne.EstadoFaixa("PA", "66000000", "66999999", "Capital repetida"));
        fonte.EstadoFaixas.Add(DadosDne.EstadoFaixa("SP", "01000000", "05999999", "Capital paulista"));
        fonte.EstadoFaixas.Add(DadosDne.EstadoFaixa("PA", " ", " ", "Sem CEP"));
        fonte.EstadoFaixas.Add(DadosDne.EstadoFaixa("XX", "10000000", "10999999", "Estado desconhecido"));
        return fonte;
    }

    private static FonteEmMemoria RecargaDasFaixasDoEstado(string versao)
    {
        FonteEmMemoria fonte = BaseComMaraba(versao);
        fonte.EstadoFaixas.Add(DadosDne.EstadoFaixa("PA", "66000000", "66999999", "Capital (Belém)"));
        fonte.EstadoFaixas.Add(DadosDne.EstadoFaixa("PA", "67000000", "68899999", "Interior do Pará"));
        fonte.EstadoFaixas.Add(DadosDne.EstadoFaixa("PA", "66000000", "66999999", "Capital repetida"));
        fonte.EstadoFaixas.Add(DadosDne.EstadoFaixa("PA", "69000000", "69999999", "Sudeste do Pará"));
        fonte.EstadoFaixas.Add(DadosDne.EstadoFaixa("SP", "01000000", "05999999", "Não deve valer"));
        return fonte;
    }

    private static FonteEmMemoria FonteDasFaixasDaCidade(string versao)
    {
        FonteEmMemoria fonte = BaseComMaraba(versao);
        AdicionarSaoPaulo(fonte);
        fonte.CidadeFaixas.Add(DadosDne.CidadeFaixa(1, "68500000", "68519999"));
        fonte.CidadeFaixas.Add(DadosDne.CidadeFaixa(1, "68500000", "68519999"));
        fonte.CidadeFaixas.Add(DadosDne.CidadeFaixa(2, "01000000", "05999999"));
        fonte.CidadeFaixas.Add(DadosDne.CidadeFaixa(1, " ", " "));
        fonte.CidadeFaixas.Add(DadosDne.CidadeFaixa(99, "10000000", "10999999"));
        return fonte;
    }

    private static FonteEmMemoria RecargaDasFaixasDaCidade(string versao)
    {
        FonteEmMemoria fonte = BaseComMaraba(versao);
        fonte.CidadeFaixas.Add(DadosDne.CidadeFaixa(1, "68500000", "68519999"));
        fonte.CidadeFaixas.Add(DadosDne.CidadeFaixa(1, "68500000", "68519999"));
        fonte.CidadeFaixas.Add(DadosDne.CidadeFaixa(1, "68520000", "68529999"));
        fonte.CidadeFaixas.Add(DadosDne.CidadeFaixa(2, "01000000", "05999999"));
        return fonte;
    }

    private static FonteEmMemoria BaseComMaraba(string versao)
    {
        FonteEmMemoria fonte = new() { Versao = versao };
        fonte.Paises.Add(DadosDne.Pais("BRA", "Brasil", "BR"));
        fonte.Estados.Add(new EstadoCruBuilder("PA", "Pará").ComCapital("Belém").NaFaixaDeCep("66000000", "68899999").Construir());
        fonte.EstadoIndicadores.Add(DadosDne.EstadoIndicador("PA", "15"));
        fonte.Cidades.Add(DadosDne.Cidade(CodigoDeMaraba, "Marabá", "PA", ddd: "94"));
        fonte.CidadeIds.Add(DadosDne.CidadeId(1, CodigoDeMaraba));
        return fonte;
    }

    private static void AdicionarSaoPaulo(FonteEmMemoria fonte)
    {
        fonte.Estados.Add(new EstadoCruBuilder("SP", "São Paulo").NaRegiao("Sudeste").ComCapital("São Paulo").NaFaixaDeCep("01000000", "19999999").Construir());
        fonte.EstadoIndicadores.Add(DadosDne.EstadoIndicador("SP", "35"));
        fonte.Cidades.Add(DadosDne.Cidade(CodigoDeSaoPaulo, "São Paulo", "SP", ddd: "11"));
        fonte.CidadeIds.Add(DadosDne.CidadeId(2, CodigoDeSaoPaulo));
    }

    private async Task LimparAsync()
    {
        await using GeoDbContext contexto = _fixture.CreateDbContext();
        await contexto.Database.ExecuteSqlRawAsync("TRUNCATE TABLE pais CASCADE");
    }
}
