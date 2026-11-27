namespace Unifesspa.Geo.IntegrationTests.Api;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Apoio dos testes de cidades: apaga o que existe e grava os dados de cada cenário. Usado por
/// composição.
/// </summary>
internal sealed class CenarioDeCidades
{
    private readonly GeoPostgisFixture _fixture;

    public CenarioDeCidades(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>Grava Pará e São Paulo com as cidades e o indicador de Marabá.</summary>
    public async Task SemearAsync()
    {
        await GeoReferenceSeed.LimparAsync(_fixture);
        await using GeoDbContext ctx = _fixture.CreateDbContext();
        Pais brasil = GeoReferenceSeed.NovoBrasil();
        Estado para = new EstadoBuilder(brasil.Id, "PA", "Pará").NaRegiao("Norte").ComCapital("Belém").Construir();
        Estado saoPaulo = new EstadoBuilder(brasil.Id, "SP", "São Paulo").NaRegiao("Sudeste").ComCapital("São Paulo").Construir();
        Cidade maraba = Maraba(para.Id);
        ctx.Paises.Add(brasil);
        ctx.Estados.AddRange(para, saoPaulo);
        ctx.Cidades.AddRange(maraba, Parauapebas(para.Id), Obidos(para.Id), SaoPauloCidade(saoPaulo.Id));
        ctx.CidadeIndicadores.Add(IndicadorDeMaraba(maraba.Id));
        await ctx.SaveChangesAsync();
    }

    /// <summary>Grava só o Pará, com as cidades que o teste montar a partir do identificador do estado.</summary>
    public async Task GravarCidadesDoParaAsync(params Func<Guid, CidadeBuilder>[] cidades)
    {
        await GeoReferenceSeed.LimparAsync(_fixture);
        await using GeoDbContext ctx = _fixture.CreateDbContext();
        Pais brasil = GeoReferenceSeed.NovoBrasil();
        Estado para = new EstadoBuilder(brasil.Id, "PA", "Pará").Construir();
        ctx.Paises.Add(brasil);
        ctx.Estados.Add(para);
        ctx.Cidades.AddRange(cidades.Select(montar => montar(para.Id).Construir()));
        await ctx.SaveChangesAsync();
    }

    /// <summary>Grava duas cidades de mesmo nome (Bonito) em Pernambuco e no Mato Grosso do Sul.</summary>
    public async Task GravarCidadesHomonimasAsync()
    {
        await GeoReferenceSeed.LimparAsync(_fixture);
        await using GeoDbContext ctx = _fixture.CreateDbContext();
        Pais brasil = GeoReferenceSeed.NovoBrasil();
        Estado pe = new EstadoBuilder(brasil.Id, "PE", "Pernambuco").Construir();
        Estado ms = new EstadoBuilder(brasil.Id, "MS", "Mato Grosso do Sul").Construir();
        Cidade bonitoPe = new CidadeBuilder(pe.Id, "PE", "2602605", "Bonito").ComNomeNormalizado("bonito").Construir();
        Cidade bonitoMs = new CidadeBuilder(ms.Id, "MS", "5002209", "Bonito").ComNomeNormalizado("bonito").Construir();
        ctx.Paises.Add(brasil);
        ctx.Estados.AddRange(pe, ms);
        ctx.Cidades.AddRange(bonitoPe, bonitoMs);
        await ctx.SaveChangesAsync();
    }

    private static Cidade Maraba(Guid paraId) =>
        new CidadeBuilder(paraId, "PA", "1500402", "Marabá")
            .ComNomeNormalizado("maraba").ComDdd("94")
            .NaPosicao(-5.36867m, -49.11731m)
            .NaMesorregiao("Sudeste Paraense").NaMicrorregiao("Marabá")
            .NaRegiaoIntermediaria("Marabá").NaRegiaoImediata("Marabá")
            .Construir();

    private static Cidade Parauapebas(Guid paraId) =>
        new CidadeBuilder(paraId, "PA", "1505536", "Parauapebas").ComNomeNormalizado("parauapebas").ComDdd("94").Construir();

    // Óbidos (PA) NÃO contém "a" no nome normalizado — prova negativa da busca q=a.
    private static Cidade Obidos(Guid paraId) =>
        new CidadeBuilder(paraId, "PA", "1505106", "Óbidos").ComNomeNormalizado("obidos").ComDdd("93").Construir();

    private static Cidade SaoPauloCidade(Guid saoPauloId) =>
        new CidadeBuilder(saoPauloId, "SP", "3550308", "São Paulo").ComNomeNormalizado("sao paulo").ComDdd("11").Construir();

    private static CidadeIndicador IndicadorDeMaraba(Guid marabaId) =>
        GeoReferenceSeed.NovoIndicador(
            marabaId, gentilico: "marabaense", areaKm2: 15128.058m, populacaoResidente: 233669,
            densidadeDemografica: 15.45m, idh: 0.668m, aniversario: "05/04");
}
