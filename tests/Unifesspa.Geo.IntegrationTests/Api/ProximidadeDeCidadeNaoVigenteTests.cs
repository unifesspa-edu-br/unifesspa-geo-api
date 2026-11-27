namespace Unifesspa.Geo.IntegrationTests.Api;

using System.Net;
using System.Net.Http;
using System.Text.Json;

using AwesomeAssertions;

using NetTopologySuite.Geometries;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Proximidade com cidade fora de vigência e unidade da distância: cidade e logradouro de cidade
/// não vigente ficam fora do resultado e a distância sai em quilômetros. Ponto de consulta
/// (-5.35, -49.13); Marabá a cerca de 2,5 km e o logradouro mais próximo a cerca de 0,8 km. A
/// collection é serial; cada teste apaga e semeia os dados.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class ProximidadeDeCidadeNaoVigenteTests
{
    private readonly GeoPostgisFixture _fixture;
    private readonly CenarioDeMarabaParaAsConsultas _cenario;

    public ProximidadeDeCidadeNaoVigenteTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _cenario = new CenarioDeMarabaParaAsConsultas(fixture);
    }

    [Fact(DisplayName = "Logradouro de cidade não vigente fica fora dos logradouros próximos")]
    public async Task LogradourosProximos_CidadeNaoVigente_FicaFora()
    {
        await _cenario.GravarAsync(AdicionarLogradourosDeCidadeVigenteENaoVigente);

        JsonElement raiz = await ConsultarAsync("/api/logradouros/proximos?lat=-5.35&long=-49.13&raioKm=50");

        raiz.EnumerateArray().Select(l => l.GetProperty("nome").GetString()).Should().Equal("Ativa");
    }

    [Fact(DisplayName = "Cidade não vigente fica fora das cidades próximas")]
    public async Task CidadesProximas_CidadeNaoVigente_FicaFora()
    {
        await _cenario.GravarAsync(AdicionarCidadeNaoVigenteComCoordenada);

        JsonElement raiz = await ConsultarAsync("/api/cidades/proximas?lat=-5.35&long=-49.13&raioKm=100");

        raiz.EnumerateArray().Select(c => c.GetProperty("nome").GetString()).Should().Equal("Marabá");
    }

    [Fact(DisplayName = "DistanciaKm do logradouro e da cidade sai em quilômetros, não em metros")]
    public async Task DistanciaKm_SaiEmQuilometros()
    {
        await _cenario.GravarAsync(AdicionarLogradourosDeCidadeVigenteENaoVigente);

        JsonElement logradouros = await ConsultarAsync("/api/logradouros/proximos?lat=-5.35&long=-49.13&raioKm=50");
        JsonElement cidades = await ConsultarAsync("/api/cidades/proximas?lat=-5.35&long=-49.13&raioKm=50");

        logradouros[0].GetProperty("distanciaKm").GetDouble().Should().BeInRange(0.6, 1.0);
        cidades[0].GetProperty("distanciaKm").GetDouble().Should().BeInRange(2.2, 2.8);
    }

    private async Task<JsonElement> ConsultarAsync(string rota)
    {
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();
        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(client, rota);
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument doc = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static Point Ponto(decimal latitude, decimal longitude) =>
        new((double)longitude, (double)latitude) { SRID = 4326 };

    private static Cidade ItupirangaNaoVigente(Cidade maraba) =>
        new CidadeBuilder(maraba.EstadoId, "PA", "1503606", "Itupiranga")
            .NaPosicao(-5.352m, -49.131m).ComCoordenada(Ponto(-5.352m, -49.131m)).ForaDeVigencia().Construir();

    private static void AdicionarCidadeNaoVigenteComCoordenada(GeoDbContext ctx, Cidade maraba) =>
        ctx.Cidades.Add(ItupirangaNaoVigente(maraba));

    // Os dois logradouros estão dentro do raio; o da cidade não vigente é o mais próximo do ponto.
    private static void AdicionarLogradourosDeCidadeVigenteENaoVigente(GeoDbContext ctx, Cidade maraba)
    {
        Cidade itupiranga = ItupirangaNaoVigente(maraba);
        ctx.Cidades.Add(itupiranga);
        ctx.Logradouros.AddRange(
            new LogradouroBuilder(maraba.Id, "PA", "68500000", "Ativa")
                .DoTipo("Rua").NaPosicao(-5.355m, -49.135m).ComCoordenada(Ponto(-5.355m, -49.135m)).Construir(),
            new LogradouroBuilder(itupiranga.Id, "PA", "68580000", "Antiga")
                .DoTipo("Rua").NaPosicao(-5.351m, -49.131m).ComCoordenada(Ponto(-5.351m, -49.131m)).Construir());
    }
}
