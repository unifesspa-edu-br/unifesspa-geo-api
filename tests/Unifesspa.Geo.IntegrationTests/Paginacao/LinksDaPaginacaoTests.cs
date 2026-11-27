namespace Unifesspa.Geo.IntegrationTests.Paginacao;

using System.Net;
using System.Net.Http;

using AwesomeAssertions;

using Unifesspa.Geo.IntegrationTests.Api;
using Unifesspa.Geo.IntegrationTests.Etl;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Os links <c>self</c>, <c>prev</c> e <c>next</c> do cabeçalho <c>Link</c> e o cabeçalho
/// <c>X-Page-Size</c> das listagens de estados e de cidades, contra a API real e o PostGIS.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class LinksDaPaginacaoTests
{
    private static readonly string[] Ufs = ["AC", "AL", "AM", "BA", "CE"];

    private readonly GeoPostgisFixture _fixture;
    private readonly CenarioDeEstados _estados;

    public LinksDaPaginacaoTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _estados = new CenarioDeEstados(fixture);
    }

    [Theory(DisplayName = "O self repete limit e direction, nessa ordem, e os demais parâmetros na ordem em que vieram")]
    [InlineData("", "")]
    [InlineData("?limit=2", "?limit=2")]
    [InlineData("?direction=next&limit=2", "?limit=2&direction=next")]
    [InlineData("?LIMIT=2", "?limit=2")]
    [InlineData("?limit=2&zeta=1&alfa=2", "?limit=2&zeta=1&alfa=2")]
    public async Task Self_EspelhaARequisicao(string consulta, string esperado)
    {
        await _estados.SemearEstadosAsync(Ufs);
        using HttpClient cliente = _fixture.Factory.CreateAuthenticatedClient();

        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(cliente, $"/api/estados{consulta}");

        CabecalhoLinkDaResposta.Endereco(resposta, "self")!.Texto.Should().Be($"http://localhost/api/estados{esperado}");
    }

    [Fact(DisplayName = "Prev e next levam só cursor e direction, sem limit, e o self da página seguinte repete o link next")]
    public async Task PrevENext_LevamCursorEDirecao_SemLimite()
    {
        await _estados.SemearEstadosAsync(Ufs);
        using HttpClient cliente = _fixture.Factory.CreateAuthenticatedClient();
        using HttpResponseMessage primeira = await GeoReferenceSeed.Obter(cliente, "/api/estados?limit=2");
        EnderecoDeNavegacao seguinte = CabecalhoLinkDaResposta.Endereco(primeira, "next")!;

        using HttpResponseMessage segunda = await GeoReferenceSeed.Obter(cliente, seguinte.Texto);

        seguinte.Nomes.Should().Equal("cursor", "direction");
        seguinte.Valor("direction").Should().Be("next");
        CabecalhoLinkDaResposta.Endereco(segunda, "prev")!.Nomes.Should().Equal("cursor", "direction");
        CabecalhoLinkDaResposta.Endereco(segunda, "prev")!.Valor("direction").Should().Be("prev");
        CabecalhoLinkDaResposta.Endereco(segunda, "self")!.Texto.Should().Be(seguinte.Texto);
    }

    [Fact(DisplayName = "Os demais parâmetros da consulta vão para o link next, em ordem, com os valores repetidos e escapados")]
    public async Task Next_PreservaOsDemaisParametros_EmOrdemEEscapados()
    {
        await _estados.SemearEstadosAsync(Ufs);
        using HttpClient cliente = _fixture.Factory.CreateAuthenticatedClient();

        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(cliente, "/api/estados?limit=2&zeta=1&tag=a%26b%3Dc&zeta=9");

        CabecalhoLinkDaResposta.Endereco(resposta, "next")!.Texto
            .Should().EndWith("&direction=next&zeta=1&zeta=9&tag=a%26b%3Dc");
    }

    [Fact(DisplayName = "Nas cidades, os filtros uf e q continuam no link next")]
    public async Task Next_DasCidades_PreservaOsFiltros()
    {
        await new CenarioDeCidades(_fixture).SemearAsync();
        using HttpClient cliente = _fixture.Factory.CreateAuthenticatedClient();

        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(cliente, "/api/cidades?q=a&uf=PA&limit=1");

        EnderecoDeNavegacao seguinte = CabecalhoLinkDaResposta.Endereco(resposta, "next")!;
        seguinte.Nomes.Should().Equal("cursor", "direction", "q", "uf");
        (seguinte.Valor("q"), seguinte.Valor("uf")).Should().Be(("a", "PA"));
    }

    [Fact(DisplayName = "Os links levam o prefixo do caminho quando a API roda atrás de um proxy")]
    public async Task Links_LevamOPrefixoDoCaminho()
    {
        await _estados.SemearEstadosAsync(Ufs);
        using HostDasListagens host = new(_fixture.Factory, new RelogioFixo());
        host.FixarPrefixoDoCaminho("/geo");
        using HttpClient cliente = host.CriarCliente();

        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(cliente, "/geo/api/estados?limit=2");

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        CabecalhoLinkDaResposta.Endereco(resposta, "self")!.Caminho.Should().Be("/geo/api/estados");
        CabecalhoLinkDaResposta.Endereco(resposta, "next")!.Caminho.Should().Be("/geo/api/estados");
    }

    [Fact(DisplayName = "X-Page-Size traz a quantidade de itens de cada página, inclusive a última, mais curta")]
    public async Task TamanhoDaPagina_EAQuantidadeDeItens()
    {
        await _estados.SemearEstadosAsync(Ufs);
        using HttpClient cliente = _fixture.Factory.CreateAuthenticatedClient();

        string[] tamanhos = await PercorrerAsync(cliente, resposta => resposta.Headers.GetValues("X-Page-Size").Single());

        tamanhos.Should().Equal("2", "2", "1");
    }

    [Fact(DisplayName = "X-Page-Size é 0 quando não há itens")]
    public async Task TamanhoDaPagina_SemItens_EZero()
    {
        await GeoReferenceSeed.LimparAsync(_fixture);
        using HttpClient cliente = _fixture.Factory.CreateAuthenticatedClient();

        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(cliente, "/api/estados");

        resposta.Headers.GetValues("X-Page-Size").Should().Equal("0");
    }

    [Fact(DisplayName = "O cabeçalho Link lista prev, next e self, nessa ordem, e omite o que não existe")]
    public async Task Cabecalho_ListaOsRelsNaOrdem_EOmiteOQueNaoExiste()
    {
        await _estados.SemearEstadosAsync(Ufs);
        using HttpClient cliente = _fixture.Factory.CreateAuthenticatedClient();

        string[] rels = await PercorrerAsync(cliente, resposta => string.Join(",", CabecalhoLinkDaResposta.Rels(resposta)));

        rels.Should().Equal("next,self", "prev,next,self", "prev,self");
    }

    [Theory(DisplayName = "Página única não tem prev nem next, nem quando os itens enchem o limite exatamente")]
    [InlineData(100)]
    [InlineData(5)]
    public async Task PaginaUnica_TemSoOSelf(int limite)
    {
        await _estados.SemearEstadosAsync(Ufs);
        using HttpClient cliente = _fixture.Factory.CreateAuthenticatedClient();

        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(cliente, $"/api/estados?limit={limite}");

        CabecalhoLinkDaResposta.Rels(resposta).Should().Equal("self");
    }

    // Segue o link next a partir de limit=2 e devolve, página a página, o que a seleção lê da resposta.
    private static async Task<string[]> PercorrerAsync(HttpClient cliente, Func<HttpResponseMessage, string> selecao)
    {
        List<string> lidos = [];
        string? rota = "/api/estados?limit=2";
        while (rota is not null)
        {
            using HttpResponseMessage pagina = await GeoReferenceSeed.Obter(cliente, rota);
            lidos.Add(selecao(pagina));
            rota = CabecalhoLinkDaResposta.Endereco(pagina, "next")?.Texto;
        }

        return [.. lidos];
    }
}
