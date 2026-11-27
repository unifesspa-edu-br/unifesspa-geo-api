namespace Unifesspa.Geo.IntegrationTests.Api;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Apoio dos testes de hierarquia e de busca de logradouro por cidade: apaga o que existe e
/// grava São Paulo e Marabá com distritos, bairros e logradouros. Usado por composição.
/// </summary>
internal sealed class CenarioDaHierarquiaDeCidades
{
    private readonly GeoPostgisFixture _fixture;

    public CenarioDaHierarquiaDeCidades(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task SemearAsync()
    {
        await GeoReferenceSeed.LimparAsync(_fixture);
        await using GeoDbContext ctx = _fixture.CreateDbContext();
        Pais brasil = GeoReferenceSeed.NovoBrasil();
        Estado saoPaulo = new EstadoBuilder(brasil.Id, "SP", "São Paulo").Construir();
        Estado para = new EstadoBuilder(brasil.Id, "PA", "Pará").Construir();
        Cidade cidadeSp = new CidadeBuilder(saoPaulo.Id, "SP", "3550308", "São Paulo").Construir();
        Cidade maraba = new CidadeBuilder(para.Id, "PA", "1500402", "Marabá").Construir();
        ctx.Paises.Add(brasil);
        ctx.Estados.AddRange(saoPaulo, para);
        ctx.Cidades.AddRange(cidadeSp, maraba);
        AdicionarDistritos(ctx, cidadeSp, maraba);
        AdicionarBairrosELogradouros(ctx, cidadeSp, maraba);
        await ctx.SaveChangesAsync();
    }

    private static void AdicionarDistritos(GeoDbContext ctx, Cidade cidadeSp, Cidade maraba)
    {
        Distrito se = GeoReferenceSeed.NovoDistrito(
            cidadeSp.Id, "SP", "Sé", latitude: -23.55052m, longitude: -46.63331m);
        Distrito pinheiros = GeoReferenceSeed.NovoDistrito(
            cidadeSp.Id, "SP", "Pinheiros", latitude: -23.56723m, longitude: -46.70103m);
        Distrito novaMaraba = GeoReferenceSeed.NovoDistrito(maraba.Id, "PA", "Nova Marabá");
        ctx.Distritos.AddRange(se, pinheiros, novaMaraba);
    }

    private static void AdicionarBairrosELogradouros(GeoDbContext ctx, Cidade cidadeSp, Cidade maraba)
    {
        Bairro se = GeoReferenceSeed.NovoBairro(cidadeSp.Id, "SP", "Sé");
        Bairro santaCecilia = GeoReferenceSeed.NovoBairro(cidadeSp.Id, "SP", "Santa Cecília");
        Bairro saude = GeoReferenceSeed.NovoBairro(
            cidadeSp.Id, "SP", "Saúde", latitude: -23.61811m, longitude: -46.63878m);
        Bairro cidadeNova = GeoReferenceSeed.NovoBairro(maraba.Id, "PA", "Cidade Nova");
        ctx.Bairros.AddRange(se, santaCecilia, saude, cidadeNova);
        AdicionarLogradouros(ctx, cidadeSp, maraba, se, santaCecilia, cidadeNova);
    }

    // O nome do logradouro não inclui o tipo; o texto cheio (com o tipo) vive no nome completo,
    // e o nome normalizado (coluna de busca) guarda o texto completo sem acento. A busca casa
    // por tipo + nome e ordena por relevância (similaridade).
    private static void AdicionarLogradouros(
        GeoDbContext ctx, Cidade cidadeSp, Cidade maraba, Bairro se, Bairro santaCecilia, Bairro cidadeNova)
    {
        ctx.Logradouros.AddRange(
            Praca(cidadeSp, "01001000", "Sé", "Praça da Sé", se),
            Rua(cidadeSp, "01225000", "Santa Cecília", "Rua Santa Cecília", santaCecilia),
            Praca(maraba, "68500010", "São Félix", "Praça São Félix", cidadeNova),
            Rua(cidadeSp, "01230000", "das Flores", "Rua das Flores", santaCecilia),
            Rua(cidadeSp, "01231000", "das Flores e Jardins", "Rua das Flores e Jardins", santaCecilia),
            AvenidaPaulista(cidadeSp, santaCecilia));
    }

    private static Logradouro Praca(Cidade cidade, string cep, string nome, string nomeCompleto, Bairro bairro) =>
        MontarLogradouro(cidade, cep, nome, "Praça", nomeCompleto, bairro);

    private static Logradouro Rua(Cidade cidade, string cep, string nome, string nomeCompleto, Bairro bairro) =>
        MontarLogradouro(cidade, cep, nome, "Rua", nomeCompleto, bairro);

    // Uma avenida sustenta os casos de abreviação de tipo ("av") e de ordem das palavras sem
    // interferir nas contagens estritas dos demais casos: a similaridade de palavras 0.6 não
    // casa "avenida paulista" com os termos "se", "praca" e "rua das flores".
    private static Logradouro AvenidaPaulista(Cidade cidadeSp, Bairro bairro) =>
        MontarLogradouro(cidadeSp, "01310100", "Paulista", "Avenida", "Avenida Paulista", bairro);

    private static Logradouro MontarLogradouro(
        Cidade cidade, string cep, string nome, string tipo, string nomeCompleto, Bairro bairro) =>
        new LogradouroBuilder(cidade.Id, cidade.Uf, cep, nome)
            .DoTipo(tipo).ComNomeCompleto(nomeCompleto).NoBairro(bairro.Id).Construir();
}
