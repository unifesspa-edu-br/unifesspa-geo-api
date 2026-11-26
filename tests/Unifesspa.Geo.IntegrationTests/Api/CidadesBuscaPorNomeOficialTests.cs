namespace Unifesspa.Geo.IntegrationTests.Api;

using System.Net;
using System.Net.Http;
using System.Text.Json;

using AwesomeAssertions;

using Unifesspa.Geo.IntegrationTests.Etl;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Municípios carregados pelo ETL com o nome oficial do IBGE e consultados pela API: o nome
/// devolvido tem o apóstrofo, e a busca ignora acento, caixa e apóstrofo. Pau D'Arco do
/// Pará e do Tocantins são registros distintos, separados pelo filtro de UF.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class CidadesBuscaPorNomeOficialTests
{
    private const string TodosOsPauDArco = "Pau D'Arco do Pará, do Tocantins e do Piauí";

    private readonly GeoPostgisFixture _fixture;

    public CidadesBuscaPorNomeOficialTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory(DisplayName = "A busca por Pau D'Arco acha o Pará e o Tocantins com ou sem apóstrofo, em qualquer caixa")]
    [InlineData("pau d'arco")]
    [InlineData("pau darco")]
    [InlineData("PAU D’ARCO")]
    [InlineData("Pau D´Arco")]
    [InlineData("PAU DARCO")]
    public async Task Busca_PauDArco_AchaOsMunicipiosDosDoisEstados(string termo)
    {
        List<string> codigos = await BuscarCodigosAsync($"q={Uri.EscapeDataString(termo)}");

        // «Pau D'Arco do Piauí» também contém «pau d'arco» no nome: o termo o encontra junto.
        codigos.Should().BeEquivalentTo(
            [FonteComMunicipiosDeApostrofo.PauDArcoPara, FonteComMunicipiosDeApostrofo.PauDArcoTocantins, FonteComMunicipiosDeApostrofo.PauDArcoDoPiaui],
            TodosOsPauDArco);
    }

    [Theory(DisplayName = "O filtro de UF separa os dois Pau D'Arco: só o município da UF escolhida é devolvido")]
    [InlineData("TO", FonteComMunicipiosDeApostrofo.PauDArcoTocantins)]
    [InlineData("PA", FonteComMunicipiosDeApostrofo.PauDArcoPara)]
    public async Task Busca_PauDArcoComFiltroDeUf_DevolveSoOMunicipioDaUf(string uf, string codigoEsperado)
    {
        List<string> codigos = await BuscarCodigosAsync($"q={Uri.EscapeDataString("pau d'arco")}&uf={uf}");

        codigos.Should().Equal(codigoEsperado);
    }

    [Theory(DisplayName = "O detalhe do município devolve o nome oficial do IBGE, com apóstrofo")]
    [InlineData(FonteComMunicipiosDeApostrofo.PauDArcoPara, "Pau D'Arco")]
    [InlineData(FonteComMunicipiosDeApostrofo.PauDArcoTocantins, "Pau D'Arco")]
    [InlineData(FonteComMunicipiosDeApostrofo.SantaBarbaraDOeste, "Santa Bárbara d'Oeste")]
    [InlineData(FonteComMunicipiosDeApostrofo.SantAnaDoLivramento, "Sant'Ana do Livramento")]
    [InlineData(FonteComMunicipiosDeApostrofo.EldoradoDoCarajas, "Eldorado do Carajás")]
    public async Task Detalhe_DevolveNomeOficial(string codigo, string nomeEsperado)
    {
        await SemearAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(client, $"/api/cidades/{codigo}");

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument doc = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("nome").GetString().Should().Be(nomeEsperado);
    }

    [Theory(DisplayName = "Santa Bárbara d'Oeste e Sant'Ana do Livramento são achadas com e sem apóstrofo")]
    [InlineData("santa barbara d'oeste", FonteComMunicipiosDeApostrofo.SantaBarbaraDOeste)]
    [InlineData("santa barbara doeste", FonteComMunicipiosDeApostrofo.SantaBarbaraDOeste)]
    [InlineData("sant'ana do livramento", FonteComMunicipiosDeApostrofo.SantAnaDoLivramento)]
    [InlineData("santana do livramento", FonteComMunicipiosDeApostrofo.SantAnaDoLivramento)]
    public async Task Busca_NomeComApostrofoNoMeio_AchaComESemApostrofo(string termo, string codigoEsperado)
    {
        List<string> codigos = await BuscarCodigosAsync($"q={Uri.EscapeDataString(termo)}");

        codigos.Should().Equal(codigoEsperado);
    }

    [Theory(DisplayName = "A busca de município sem apóstrofo continua igual: «maraba» acha só Marabá, com ou sem acento e caixa")]
    [InlineData("maraba")]
    [InlineData("MARABÁ")]
    public async Task Busca_NomeSemApostrofo_ContinuaIgual(string termo)
    {
        List<string> codigos = await BuscarCodigosAsync($"q={Uri.EscapeDataString(termo)}");

        codigos.Should().Equal(FonteComMunicipiosDeApostrofo.Maraba);
    }

    private async Task<List<string>> BuscarCodigosAsync(string consulta)
    {
        await SemearAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();
        return await ListaDaApi.CamposAsync(client, $"/api/cidades?{consulta}&limit=100", "codigoIbge");
    }

    private async Task SemearAsync()
    {
        await GeoReferenceSeed.LimparAsync(_fixture);
        await CargaPelaEtl.ExecutarAsync(_fixture, FonteComMunicipiosDeApostrofo.Criar());
    }
}
