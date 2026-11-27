namespace Unifesspa.Geo.IntegrationTests.Api;

using NetTopologySuite.Geometries;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Apoio dos testes de proximidade: apaga o que existe e grava cidades e logradouros com e sem
/// coordenada, perto e longe do ponto de consulta (-5.35, -49.13). Usado por composição.
/// </summary>
internal sealed class CenarioDeProximidade
{
    private readonly GeoPostgisFixture _fixture;

    public CenarioDeProximidade(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task SemearAsync()
    {
        await GeoReferenceSeed.LimparAsync(_fixture);
        await using GeoDbContext ctx = _fixture.CreateDbContext();
        Pais brasil = GeoReferenceSeed.NovoBrasil();
        Estado para = new EstadoBuilder(brasil.Id, "PA", "Pará").Construir();
        Estado saoPauloUf = new EstadoBuilder(brasil.Id, "SP", "São Paulo").Construir();
        Cidade maraba = Maraba(para.Id);
        Cidade saoPaulo = SaoPaulo(saoPauloUf.Id);
        ctx.Paises.Add(brasil);
        ctx.Estados.AddRange(para, saoPauloUf);
        ctx.Cidades.AddRange(maraba, Eldorado(para.Id), saoPaulo, SemCoordenada(para.Id), CoordenadaSemLatitudeELongitude(para.Id));
        ctx.Logradouros.AddRange(RuaA(maraba.Id), AvenidaB(maraba.Id), PracaDaSe(saoPaulo.Id));
        await ctx.SaveChangesAsync();
    }

    private static Point Ponto(decimal latitude, decimal longitude) =>
        new((double)longitude, (double)latitude) { SRID = 4326 };

    // Próximas ao ponto de consulta (-5.35, -49.13).
    private static Cidade Maraba(Guid paraId) =>
        new CidadeBuilder(paraId, "PA", "1500402", "Marabá")
            .NaPosicao(-5.36867m, -49.11731m).ComCoordenada(Ponto(-5.36867m, -49.11731m)).Construir();

    private static Cidade Eldorado(Guid paraId) =>
        new CidadeBuilder(paraId, "PA", "1502608", "Eldorado dos Carajás")
            .NaPosicao(-6.10m, -49.36m).ComCoordenada(Ponto(-6.10m, -49.36m)).Construir();

    // Longe (> 100 km).
    private static Cidade SaoPaulo(Guid saoPauloUfId) =>
        new CidadeBuilder(saoPauloUfId, "SP", "3550308", "São Paulo")
            .NaPosicao(-23.55m, -46.63m).ComCoordenada(Ponto(-23.55m, -46.63m)).Construir();

    // Próxima ao ponto, mas sem coordenada: não aparece.
    private static Cidade SemCoordenada(Guid paraId) =>
        new CidadeBuilder(paraId, "PA", "1500800", "Bom Jesus do Tocantins").Construir();

    // Coordenada presente, mas latitude e longitude nulas: também excluída.
    private static Cidade CoordenadaSemLatitudeELongitude(Guid paraId) =>
        new CidadeBuilder(paraId, "PA", "1500909", "Brejo Grande do Araguaia")
            .ComCoordenada(Ponto(-5.36m, -49.12m)).Construir();

    // Logradouros próximos, em Marabá.
    private static Logradouro RuaA(Guid marabaId) =>
        new LogradouroBuilder(marabaId, "PA", "68500000", "A")
            .DoTipo("Rua").ComNomeCompleto("Rua A")
            .NaPosicao(-5.355m, -49.135m).ComCoordenada(Ponto(-5.355m, -49.135m)).Construir();

    private static Logradouro AvenidaB(Guid marabaId) =>
        new LogradouroBuilder(marabaId, "PA", "68500100", "B")
            .DoTipo("Avenida").ComNomeCompleto("Avenida B")
            .NaPosicao(-5.40m, -49.20m).ComCoordenada(Ponto(-5.40m, -49.20m)).Construir();

    // Logradouro longe, em São Paulo.
    private static Logradouro PracaDaSe(Guid saoPauloId) =>
        new LogradouroBuilder(saoPauloId, "SP", "01001000", "da Sé")
            .DoTipo("Praça").ComNomeCompleto("Praça da Sé")
            .NaPosicao(-23.55m, -46.63m).ComCoordenada(Ponto(-23.55m, -46.63m)).Construir();
}
