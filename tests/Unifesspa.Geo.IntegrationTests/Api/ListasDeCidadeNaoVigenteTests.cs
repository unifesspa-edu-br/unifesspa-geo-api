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
/// Listas e detalhe de cidade com registros fora de vigência: a cidade não vigente é tratada como
/// inexistente (404), o distrito, o bairro, o logradouro e o indicador não vigentes não aparecem e
/// o logradouro de um bairro não vigente sai sem o nome do bairro. A collection é serial; cada teste
/// apaga e semeia os dados.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class ListasDeCidadeNaoVigenteTests
{
    private readonly GeoPostgisFixture _fixture;
    private readonly CenarioDeMarabaParaAsConsultas _cenario;

    public ListasDeCidadeNaoVigenteTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _cenario = new CenarioDeMarabaParaAsConsultas(fixture);
    }

    [Theory(DisplayName = "Cidade não vigente dá 404 nas listas de distritos, bairros e logradouros e no detalhe")]
    [InlineData("/api/cidades/1500402")]
    [InlineData("/api/cidades/1500402/distritos")]
    [InlineData("/api/cidades/1500402/bairros")]
    [InlineData("/api/cidades/1500402/bairros?q=cidade")]
    [InlineData("/api/cidades/1500402/logradouros")]
    [InlineData("/api/cidades/1500402/logradouros?q=rua%20ativa")]
    public async Task CidadeNaoVigente_Da404(string rota)
    {
        await _cenario.GravarComCidadeForaDeVigenciaAsync(AdicionarRegistrosVigentesDaCidade);
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(client, rota);

        resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact(DisplayName = "Distrito não vigente fica fora da lista de distritos da cidade")]
    public async Task Distritos_NaoVigenteFicaFora()
    {
        await _cenario.GravarAsync(AdicionarDistritoVigenteEDistritoNaoVigente);

        List<string> nomes = await CamposAsync("/api/cidades/1500402/distritos?limit=100", "nome");

        nomes.Should().Equal("Nova Marabá");
    }

    [Fact(DisplayName = "Bairro não vigente fica fora da lista de bairros, com e sem busca")]
    public async Task Bairros_NaoVigenteFicaFora()
    {
        await _cenario.GravarAsync(AdicionarBairroVigenteEBairroNaoVigente);

        List<string> semBusca = await CamposAsync("/api/cidades/1500402/bairros?limit=100", "nome");
        List<string> comBusca = await CamposAsync("/api/cidades/1500402/bairros?q=cidade&limit=100", "nome");

        semBusca.Should().Equal("Cidade Nova");
        comBusca.Should().Equal("Cidade Nova");
    }

    [Fact(DisplayName = "Logradouro não vigente fica fora da lista de logradouros, com e sem busca")]
    public async Task Logradouros_NaoVigenteFicaFora()
    {
        await _cenario.GravarAsync(AdicionarLogradouroVigenteELogradouroNaoVigente);

        List<string> semBusca = await CamposAsync("/api/cidades/1500402/logradouros?limit=100", "nome");
        List<string> comBusca = await CamposAsync("/api/cidades/1500402/logradouros?q=rua%20ativa&limit=100", "nome");

        semBusca.Should().Equal("Rua Ativa");
        comBusca.Should().Equal("Rua Ativa");
    }

    [Fact(DisplayName = "Logradouro de bairro não vigente sai sem o nome do bairro, com e sem busca")]
    public async Task Logradouros_BairroNaoVigente_SaiSemNomeDoBairro()
    {
        await _cenario.GravarAsync(AdicionarLogradourosEmBairroVigenteENaoVigente);

        List<string> semBusca = await CamposAsync("/api/cidades/1500402/logradouros?limit=100", "bairro");
        List<string> comBusca = await CamposAsync("/api/cidades/1500402/logradouros?q=rua&limit=100", "bairro");

        semBusca.Should().BeEquivalentTo(new string?[] { "Cidade Nova", null });
        comBusca.Should().BeEquivalentTo(new string?[] { "Cidade Nova", null });
    }

    [Fact(DisplayName = "Detalhe da cidade com indicador não vigente traz o indicador nulo")]
    public async Task Detalhe_IndicadorNaoVigente_TrazIndicadorNulo()
    {
        await _cenario.GravarAsync(AdicionarIndicadorNaoVigente);
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(client, "/api/cidades/1500402");

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument doc = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("nome").GetString().Should().Be("Marabá");
        doc.RootElement.GetProperty("indicador").ValueKind.Should().Be(JsonValueKind.Null);
    }

    private async Task<List<string>> CamposAsync(string rota, string campo)
    {
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();
        return await ListaDaApi.CamposAsync(client, rota, campo);
    }

    private static void AdicionarRegistrosVigentesDaCidade(GeoDbContext ctx, Cidade cidade)
    {
        Bairro cidadeNova = GeoReferenceSeed.NovoBairro(cidade.Id, "PA", "Cidade Nova");
        ctx.Distritos.Add(GeoReferenceSeed.NovoDistrito(cidade.Id, "PA", "Nova Marabá"));
        ctx.Bairros.Add(cidadeNova);
        ctx.Logradouros.Add(CenarioDeMarabaParaAsConsultas.LogradouroEm(cidade, "68501000", "Rua Ativa", "Rua", bairro: cidadeNova));
    }

    private static void AdicionarDistritoVigenteEDistritoNaoVigente(GeoDbContext ctx, Cidade cidade)
    {
        ctx.Distritos.Add(GeoReferenceSeed.NovoDistrito(cidade.Id, "PA", "Nova Marabá"));
        ctx.Distritos.Add(GeoReferenceSeed.NovoDistrito(cidade.Id, "PA", "Distrito Antigo", vigente: false));
    }

    private static void AdicionarBairroVigenteEBairroNaoVigente(GeoDbContext ctx, Cidade cidade)
    {
        ctx.Bairros.Add(GeoReferenceSeed.NovoBairro(cidade.Id, "PA", "Cidade Nova"));
        ctx.Bairros.Add(GeoReferenceSeed.NovoBairro(cidade.Id, "PA", "Cidade Antiga", vigente: false));
    }

    // O logradouro não vigente tem nome quase igual ao vigente: sem o filtro de vigência, a busca
    // o devolveria junto.
    private static void AdicionarLogradouroVigenteELogradouroNaoVigente(GeoDbContext ctx, Cidade cidade)
    {
        ctx.Logradouros.Add(CenarioDeMarabaParaAsConsultas.LogradouroEm(cidade, "68501000", "Rua Ativa", "Rua"));
        ctx.Logradouros.Add(NaoVigente(cidade, "68502000", "Rua Ativa Antiga"));
    }

    private static void AdicionarLogradourosEmBairroVigenteENaoVigente(GeoDbContext ctx, Cidade cidade)
    {
        Bairro vigente = GeoReferenceSeed.NovoBairro(cidade.Id, "PA", "Cidade Nova");
        Bairro antigo = GeoReferenceSeed.NovoBairro(cidade.Id, "PA", "Cidade Antiga", vigente: false);
        ctx.Bairros.AddRange(vigente, antigo);
        ctx.Logradouros.AddRange(
            CenarioDeMarabaParaAsConsultas.LogradouroEm(cidade, "68501000", "Rua Um", "Rua", bairro: vigente),
            CenarioDeMarabaParaAsConsultas.LogradouroEm(cidade, "68502000", "Rua Dois", "Rua", bairro: antigo));
    }

    private static void AdicionarIndicadorNaoVigente(GeoDbContext ctx, Cidade cidade) =>
        ctx.CidadeIndicadores.Add(GeoTestKeys.Ok(CidadeIndicador.Importar(
            cidade.Id, "marabaense", null, null, new MetricasDaCidade(), GeoReferenceSeed.Proveniencia(vigente: false))));

    private static Logradouro NaoVigente(Cidade cidade, string cep, string nome) =>
        new LogradouroBuilder(cidade.Id, cidade.Uf, cep, nome).DoTipo("Rua").ForaDeVigencia().Construir();
}
