namespace Unifesspa.Geo.IntegrationTests.Api;

using System.Net;
using System.Net.Http;
using System.Text.Json;

using AwesomeAssertions;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Domain.ValueObjects;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Consulta de CEP pelo CEP geral, pelo grande usuário e pelas faixas: a coordenada do CEP
/// geral cai na da cidade campo a campo, o distrito não vigente rebaixa o nível, o grande
/// usuário sem faixa segue a cascata, a faixa de outra cidade não vaza e o bairro vence o
/// distrito. Cidade de Marabá com latitude -5.36867 e longitude -49.11731. A collection é
/// serial; cada teste apaga e semeia os dados.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class CepGeralEGrandeUsuarioNaCascataTests
{
    private const decimal LatitudeDeMaraba = -5.36867m;
    private const decimal LongitudeDeMaraba = -49.11731m;

    private readonly GeoPostgisFixture _fixture;
    private readonly CenarioDeMarabaParaAsConsultas _cenario;

    public CepGeralEGrandeUsuarioNaCascataTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _cenario = new CenarioDeMarabaParaAsConsultas(fixture);
    }

    [Fact(DisplayName = "CEP geral sem coordenada própria usa a latitude e a longitude da cidade")]
    public async Task CepGeral_SemCoordenada_UsaAsDaCidade()
    {
        await _cenario.GravarAsync(
            (ctx, cidade) => ctx.CepGerais.Add(CepGeralNaPosicao("68500000", cidade, null, null)));

        JsonElement raiz = await ResolverAsync("68500000");

        raiz.GetProperty("origem").GetString().Should().Be("cep-geral");
        raiz.GetProperty("latitude").GetDecimal().Should().Be(LatitudeDeMaraba);
        raiz.GetProperty("longitude").GetDecimal().Should().Be(LongitudeDeMaraba);
    }

    [Fact(DisplayName = "CEP geral só com latitude própria mantém a latitude e toma a longitude da cidade")]
    public async Task CepGeral_SoComLatitude_TomaALongitudeDaCidade()
    {
        await _cenario.GravarAsync(
            (ctx, cidade) => ctx.CepGerais.Add(CepGeralNaPosicao("68500000", cidade, -5.40m, null)));

        JsonElement raiz = await ResolverAsync("68500000");

        raiz.GetProperty("latitude").GetDecimal().Should().Be(-5.40m);
        raiz.GetProperty("longitude").GetDecimal().Should().Be(LongitudeDeMaraba);
    }

    [Fact(DisplayName = "CEP geral só com longitude própria mantém a longitude e toma a latitude da cidade")]
    public async Task CepGeral_SoComLongitude_TomaALatitudeDaCidade()
    {
        await _cenario.GravarAsync(
            (ctx, cidade) => ctx.CepGerais.Add(CepGeralNaPosicao("68500000", cidade, null, -49.20m)));

        JsonElement raiz = await ResolverAsync("68500000");

        raiz.GetProperty("latitude").GetDecimal().Should().Be(LatitudeDeMaraba);
        raiz.GetProperty("longitude").GetDecimal().Should().Be(-49.20m);
    }

    [Fact(DisplayName = "CEP geral de distrito não vigente resolve no nível cidade, sem distrito")]
    public async Task CepGeral_DistritoNaoVigente_ResolveNoNivelCidade()
    {
        await _cenario.GravarAsync(AdicionarCepGeralDeDistritoNaoVigente);

        JsonElement raiz = await ResolverAsync("68508900");

        raiz.GetProperty("origem").GetString().Should().Be("cep-geral");
        raiz.GetProperty("nivelResolucao").GetString().Should().Be("cidade");
        raiz.GetProperty("distrito").GetString().Should().BeNull();
        raiz.GetProperty("cidade").GetString().Should().Be("Marabá");
    }

    [Fact(DisplayName = "Grande usuário sem faixa de cidade e sem CEP geral não resolve (404)")]
    public async Task GrandeUsuario_SemFaixa_SegueACascataAte404()
    {
        await _cenario.GravarAsync(
            (ctx, _) => ctx.CepGrandesUsuarios.Add(GeoReferenceSeed.NovoGrandeUsuario("68509000", "Prefeitura de Marabá")));
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(client, "/api/cep/68509000");

        resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact(DisplayName = "Grande usuário sem faixa de cidade segue a cascata e resolve pelo CEP geral")]
    public async Task GrandeUsuario_SemFaixa_ResolvePeloCepGeral()
    {
        await _cenario.GravarAsync(AdicionarGrandeUsuarioECepGeralSemFaixa);

        JsonElement raiz = await ResolverAsync("68509000");

        raiz.GetProperty("origem").GetString().Should().Be("cep-geral");
        raiz.GetProperty("logradouro").GetString().Should().BeNull();
    }

    [Fact(DisplayName = "Faixa de bairro e de distrito de outra cidade não vaza para a cidade da faixa")]
    public async Task Faixa_DeOutraCidade_NaoVaza()
    {
        await _cenario.GravarAsync(AdicionarFaixasDeBairroEDistritoDeOutraCidade);

        JsonElement doBairro = await ResolverAsync("68507100");
        JsonElement doDistrito = await ResolverAsync("68508100");

        ConferirSoFaixaDaCidade(doBairro);
        ConferirSoFaixaDaCidade(doDistrito);
    }

    [Fact(DisplayName = "Faixa de bairro e de distrito na mesma cidade: o bairro vence e o distrito acompanha")]
    public async Task Faixa_BairroVenceODistrito()
    {
        await _cenario.GravarAsync(AdicionarFaixasDeBairroEDistritoDaMesmaCidade);

        JsonElement raiz = await ResolverAsync("68507500");

        raiz.GetProperty("origem").GetString().Should().Be("faixa-bairro");
        raiz.GetProperty("nivelResolucao").GetString().Should().Be("bairro");
        raiz.GetProperty("bairro").GetString().Should().Be("Cidade Nova");
        raiz.GetProperty("distrito").GetString().Should().Be("Nova Marabá");
    }

    private static void ConferirSoFaixaDaCidade(JsonElement raiz)
    {
        raiz.GetProperty("cidade").GetString().Should().Be("Marabá");
        raiz.GetProperty("origem").GetString().Should().Be("faixa-cidade");
        raiz.GetProperty("bairro").GetString().Should().BeNull();
        raiz.GetProperty("distrito").GetString().Should().BeNull();
    }

    private async Task<JsonElement> ResolverAsync(string cep)
    {
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();
        return await RespostaDoCep.ResolverAsync(client, $"/api/cep/{cep}");
    }

    private static CepGeral CepGeralNaPosicao(string cep, Cidade cidade, decimal? latitude, decimal? longitude) =>
        GeoTestKeys.Ok(CepGeral.Importar(
            cep, cidade.Id, null, cidade.Uf,
            new PosicaoGeografica(latitude, longitude, null), GeoReferenceSeed.Proveniencia()));

    private static void AdicionarCepGeralDeDistritoNaoVigente(GeoDbContext ctx, Cidade cidade)
    {
        Distrito antigo = GeoReferenceSeed.NovoDistrito(cidade.Id, "PA", "Distrito Antigo", vigente: false);
        ctx.Distritos.Add(antigo);
        ctx.CepGerais.Add(GeoReferenceSeed.NovoCepGeral("68508900", cidade.Id, "PA", antigo.Id));
    }

    private static void AdicionarGrandeUsuarioECepGeralSemFaixa(GeoDbContext ctx, Cidade cidade)
    {
        ctx.CepGrandesUsuarios.Add(GeoReferenceSeed.NovoGrandeUsuario("68509000", "Prefeitura de Marabá"));
        ctx.CepGerais.Add(GeoReferenceSeed.NovoCepGeral("68509000", cidade.Id, "PA"));
    }

    // A faixa de cidade só existe para Marabá; os CEPs 68507xxx e 68508xxx também estão nas
    // faixas de bairro e de distrito de Parauapebas, que não podem aparecer na resposta.
    private static void AdicionarFaixasDeBairroEDistritoDeOutraCidade(GeoDbContext ctx, Cidade maraba)
    {
        Cidade parauapebas = new CidadeBuilder(maraba.EstadoId, "PA", "1505536", "Parauapebas").Construir();
        Bairro centro = GeoReferenceSeed.NovoBairro(parauapebas.Id, "PA", "Centro");
        Distrito rural = GeoReferenceSeed.NovoDistrito(parauapebas.Id, "PA", "Zona Rural");
        ctx.Cidades.Add(parauapebas);
        ctx.Bairros.Add(centro);
        ctx.Distritos.Add(rural);
        ctx.CidadeFaixasCep.Add(GeoReferenceSeed.NovaCidadeFaixa(maraba.Id, "68500000", "68599999"));
        ctx.BairroFaixasCep.Add(GeoReferenceSeed.NovaBairroFaixa(centro.Id, "68507000", "68507999"));
        ctx.DistritoFaixasCep.Add(GeoReferenceSeed.NovaDistritoFaixa(rural.Id, "68508000", "68508999"));
    }

    private static void AdicionarFaixasDeBairroEDistritoDaMesmaCidade(GeoDbContext ctx, Cidade maraba)
    {
        Bairro cidadeNova = GeoReferenceSeed.NovoBairro(maraba.Id, "PA", "Cidade Nova");
        Distrito novaMaraba = GeoReferenceSeed.NovoDistrito(maraba.Id, "PA", "Nova Marabá");
        ctx.Bairros.Add(cidadeNova);
        ctx.Distritos.Add(novaMaraba);
        ctx.CidadeFaixasCep.Add(GeoReferenceSeed.NovaCidadeFaixa(maraba.Id, "68500000", "68599999"));
        ctx.BairroFaixasCep.Add(GeoReferenceSeed.NovaBairroFaixa(cidadeNova.Id, "68507000", "68507999"));
        ctx.DistritoFaixasCep.Add(GeoReferenceSeed.NovaDistritoFaixa(novaMaraba.Id, "68507000", "68507999"));
    }
}
