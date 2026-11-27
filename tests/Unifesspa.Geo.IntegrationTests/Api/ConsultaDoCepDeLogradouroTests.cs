namespace Unifesspa.Geo.IntegrationTests.Api;

using System.Net;
using System.Net.Http;
using System.Text.Json;

using AwesomeAssertions;

using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Consulta de CEP que resolve por logradouro, contra a API real e o PostGIS: endereço
/// completo, complemento, desempate entre logradouros do mesmo CEP e logradouro não vigente.
/// A collection é serial; cada teste apaga e semeia os dados.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class ConsultaDoCepDeLogradouroTests
{
    private readonly GeoPostgisFixture _fixture;
    private readonly CenarioDaConsultaDoCep _cenario;

    public ConsultaDoCepDeLogradouroTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _cenario = new CenarioDaConsultaDoCep(fixture);
    }

    [Fact(DisplayName = "CA-01: CEP de logradouro resolve logradouro+bairro+cidade+UF+coordenada e _links")]
    public async Task Cep_ResolveLogradouro()
    {
        await _cenario.PrepararAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        JsonElement raiz = await RespostaDoCep.ResolverAsync(client, "/api/cep/01001000");

        AfirmarEnderecoDaPracaDaSe(raiz);
        raiz.GetProperty("nivelResolucao").GetString().Should().Be("logradouro");
        raiz.GetProperty("origem").GetString().Should().Be("logradouro");
        raiz.GetProperty("alternativos").GetArrayLength().Should().Be(0);

        JsonElement links = raiz.GetProperty("_links");
        links.GetProperty("cidade").GetString().Should().Be("/api/cidades/3550308");
        links.GetProperty("estado").GetString().Should().Be("/api/estados/SP");
    }

    [Fact(DisplayName = "CA-01: complemento único do CEP é exposto")]
    public async Task Cep_ComplementoUnico()
    {
        await _cenario.PrepararAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(client, "/api/cep/01001000");
        using JsonDocument doc = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("complemento").GetString().Should().Be("de 1 a 999 - lado ímpar");
    }

    [Fact(DisplayName = "CEP com mais de um complemento: campo complemento fica null (ambíguo)")]
    public async Task Cep_MultiplosComplementos_Null()
    {
        await _cenario.PrepararAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        JsonElement raiz = await RespostaDoCep.ResolverAsync(client, "/api/cep/01002000");

        JsonElement complemento = raiz.GetProperty("complemento");
        complemento.ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact(DisplayName = "CA-02: CEP com vários logradouros traz primário + alternativos, estável entre execuções")]
    public async Task Cep_MultiplosLogradouros_DesempateEstavel()
    {
        await _cenario.PrepararAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        JsonElement primeira = await RespostaDoCep.ResolverAsync(client, "/api/cep/01310100");
        primeira.GetProperty("nivelResolucao").GetString().Should().Be("logradouro");
        JsonElement segunda = await RespostaDoCep.ResolverAsync(client, "/api/cep/01310100");
        segunda.GetProperty("nivelResolucao").GetString().Should().Be("logradouro");

        // Desempate (nome_normalizado, ...): "alameda santos" < "avenida paulista".
        LogradouroPrimario(primeira).Should().Be("Alameda Santos");
        LogradourosAlternativos(primeira).Should().ContainSingle().Which.Should().Be("Avenida Paulista");
        LogradouroPrimario(segunda).Should().Be(LogradouroPrimario(primeira), "o primário é determinístico entre execuções");
        LogradourosAlternativos(segunda).Should().Equal(LogradourosAlternativos(primeira), "a ordem dos alternativos é estável");
    }

    [Fact(DisplayName = "ADR-0092: logradouro stale (vigente=false) não resolve (404 quando nada mais cobre)")]
    public async Task Cep_LogradouroStale_NaoResolve()
    {
        await _cenario.PrepararAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        // 30000000 só tem um logradouro stale e não está em nenhuma faixa → 404.
        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(client, "/api/cep/30000000");
        resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static void AfirmarEnderecoDaPracaDaSe(JsonElement raiz)
    {
        raiz.GetProperty("cep").GetString().Should().Be("01001000");
        raiz.GetProperty("tipo").GetString().Should().Be("Praça");
        raiz.GetProperty("logradouro").GetString().Should().Be("Praça da Sé");
        raiz.GetProperty("bairro").GetString().Should().Be("Sé");
        raiz.GetProperty("cidade").GetString().Should().Be("São Paulo");
        raiz.GetProperty("codigoIbge").GetString().Should().Be("3550308");
        raiz.GetProperty("uf").GetString().Should().Be("SP");
        raiz.GetProperty("latitude").GetDecimal().Should().Be(-23.55m);
        raiz.GetProperty("longitude").GetDecimal().Should().Be(-46.63m);
    }

    private static string LogradouroPrimario(JsonElement raiz)
    {
        return raiz.GetProperty("logradouro").GetString()!;
    }

    private static IReadOnlyList<string> LogradourosAlternativos(JsonElement raiz)
    {
        return [.. raiz.GetProperty("alternativos")
            .EnumerateArray()
            .Select(a => a.GetProperty("logradouro").GetString()!)];
    }
}
