namespace Unifesspa.Geo.IntegrationTests.Api;

using System.Net.Http;
using System.Text.Json;

using AwesomeAssertions;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Consulta de CEP que resolve por faixa (cidade, bairro, distrito), por CEP geral ou por
/// grande usuário, contra a API real e o PostGIS. A collection é serial; cada teste apaga e
/// semeia os dados.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class ConsultaDoCepPorFaixaECepGeralTests
{
    private readonly GeoPostgisFixture _fixture;
    private readonly CenarioDaConsultaDoCep _cenario;

    public ConsultaDoCepPorFaixaECepGeralTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _cenario = new CenarioDaConsultaDoCep(fixture);
    }

    [Fact(DisplayName = "CA-03: CEP geral sem logradouro resolve cidade por faixa (nível cidade, sem logradouro)")]
    public async Task Cep_ResolvePorFaixaCidade()
    {
        await _cenario.PrepararAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        JsonElement raiz = await RespostaDoCep.ResolverAsync(client, "/api/cep/68500005");

        raiz.GetProperty("cidade").GetString().Should().Be("Marabá");
        raiz.GetProperty("uf").GetString().Should().Be("PA");
        raiz.GetProperty("origem").GetString().Should().Be("faixa-cidade");
        raiz.GetProperty("nivelResolucao").GetString().Should().Be("cidade");
        raiz.GetProperty("logradouro").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact(DisplayName = "CEP geral da cidade resolve como cep-geral (exato), nível cidade, sem distrito")]
    public async Task Cep_ResolveCepGeralDaCidade()
    {
        await _cenario.PrepararAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        JsonElement raiz = await RespostaDoCep.ResolverAsync(client, "/api/cep/68500000");

        raiz.GetProperty("cidade").GetString().Should().Be("Marabá");
        raiz.GetProperty("origem").GetString().Should().Be("cep-geral");
        raiz.GetProperty("nivelResolucao").GetString().Should().Be("cidade");
        raiz.GetProperty("distrito").ValueKind.Should().Be(JsonValueKind.Null);
        raiz.GetProperty("logradouro").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact(DisplayName = "CEP geral do distrito resolve como cep-geral, nível distrito, com o nome do distrito (antes da faixa-distrito)")]
    public async Task Cep_ResolveCepGeralDoDistrito()
    {
        await _cenario.PrepararAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        // 68508900 também está na faixa do distrito Nova Marabá: o CEP oficial prevalece.
        JsonElement raiz = await RespostaDoCep.ResolverAsync(client, "/api/cep/68508900");

        raiz.GetProperty("origem").GetString().Should().Be("cep-geral");
        raiz.GetProperty("nivelResolucao").GetString().Should().Be("distrito");
        raiz.GetProperty("distrito").GetString().Should().Be("Nova Marabá");
    }

    [Theory(DisplayName = "Cascata: CEP que também é geral resolve antes como logradouro ou grande usuário")]
    [InlineData("01001000", "logradouro")]
    [InlineData("01051900", "grande-usuario")]
    public async Task Cep_LogradouroEGrandeUsuarioPrecedemCepGeral(string cep, string origemEsperada)
    {
        await _cenario.PrepararAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(client, $"/api/cep/{cep}");

        using JsonDocument doc = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("origem").GetString().Should().Be(origemEsperada);
    }

    [Fact(DisplayName = "CEP geral stale (não vigente) não resolve como cep-geral: volta para a faixa")]
    public async Task Cep_CepGeralStale_CaiNaFaixa()
    {
        await _cenario.PrepararAsync();
        await using (GeoDbContext ctx = _fixture.CreateDbContext())
        {
            Cidade maraba = ctx.Cidades.Single(c => c.CodigoIbge == "1500402");
            ctx.CepGerais.Add(GeoReferenceSeed.NovoCepGeral("68500500", maraba.Id, "PA", vigente: false));
            await ctx.SaveChangesAsync();
        }

        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();
        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(client, "/api/cep/68500500");

        using JsonDocument doc = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("origem").GetString().Should().Be("faixa-cidade");
    }

    [Fact(DisplayName = "CA-03: faixa de bairro mais específica eleva o nível para bairro")]
    public async Task Cep_ResolvePorFaixaBairro()
    {
        await _cenario.PrepararAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        JsonElement raiz = await RespostaDoCep.ResolverAsync(client, "/api/cep/68507100");

        raiz.GetProperty("cidade").GetString().Should().Be("Marabá");
        raiz.GetProperty("bairro").GetString().Should().Be("Cidade Nova");
        raiz.GetProperty("origem").GetString().Should().Be("faixa-bairro");
        raiz.GetProperty("nivelResolucao").GetString().Should().Be("bairro");
    }

    [Fact(DisplayName = "CA-03: faixa de distrito (sem bairro cobrindo) eleva o nível para distrito")]
    public async Task Cep_ResolvePorFaixaDistrito()
    {
        await _cenario.PrepararAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        // 68508100 está na faixa de cidade (Marabá) e na faixa de distrito (Nova Marabá),
        // mas NÃO na faixa de bairro (68507000-68507999) → resolve no nível distrito.
        JsonElement raiz = await RespostaDoCep.ResolverAsync(client, "/api/cep/68508100");

        raiz.GetProperty("cidade").GetString().Should().Be("Marabá");
        raiz.GetProperty("distrito").GetString().Should().Be("Nova Marabá");
        raiz.GetProperty("bairro").ValueKind.Should().Be(JsonValueKind.Null);
        raiz.GetProperty("origem").GetString().Should().Be("faixa-distrito");
        raiz.GetProperty("nivelResolucao").GetString().Should().Be("distrito");
    }

    [Fact(DisplayName = "CA-04: CEP de grande usuário traz nome + cidade/UF da faixa CEP")]
    public async Task Cep_ResolveGrandeUsuario_CidadeDaFaixa()
    {
        await _cenario.PrepararAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        JsonElement raiz = await RespostaDoCep.ResolverAsync(client, "/api/cep/01051900");

        raiz.GetProperty("origem").GetString().Should().Be("grande-usuario");
        raiz.GetProperty("logradouro").GetString().Should().Be("Banco do Brasil Agência Centro");
        raiz.GetProperty("cidade").GetString().Should().Be("São Paulo");
        raiz.GetProperty("uf").GetString().Should().Be("SP");
        raiz.GetProperty("nivelResolucao").GetString().Should().Be("cidade");
    }
}
