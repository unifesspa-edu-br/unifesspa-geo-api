namespace Unifesspa.Geo.IntegrationTests.Paginacao;

using System.Net.Http;

using AwesomeAssertions;

using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Navegação para a frente e para trás nos distritos de uma cidade, listagem paginada só pelo
/// identificador, contra a API real e o PostGIS.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class NavegacaoPorIdTests
{
    private const string Distritos = "/api/cidades/" + CenarioDeDistritosPaginados.CodigoIbgeDeMaraba + "/distritos";

    private readonly GeoPostgisFixture _fixture;
    private readonly CenarioDeDistritosPaginados _cenario;

    public NavegacaoPorIdTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _cenario = new CenarioDeDistritosPaginados(fixture);
    }

    [Fact(DisplayName = "A primeira página não tem prev e tem next")]
    public async Task PrimeiraPagina_NaoTemPrev_ETemNext()
    {
        await _cenario.SemearAsync(5);
        using HttpClient cliente = _fixture.Factory.CreateAuthenticatedClient();

        PaginaDeDistritos pagina = await PaginaDeDistritos.LerAsync(cliente, $"{Distritos}?limit=2");

        pagina.Ids.Should().HaveCount(2);
        pagina.Anterior.Should().BeNull();
        pagina.Seguinte.Should().NotBeNull();
    }

    [Fact(DisplayName = "O prev da segunda página volta à primeira, com os mesmos itens e sem repetir os da segunda")]
    public async Task Prev_DaSegundaPagina_VoltaAPrimeiraSemRepetir()
    {
        await _cenario.SemearAsync(5);
        using HttpClient cliente = _fixture.Factory.CreateAuthenticatedClient();
        PaginaDeDistritos primeira = await PaginaDeDistritos.LerAsync(cliente, $"{Distritos}?limit=2");
        PaginaDeDistritos segunda = await PaginaDeDistritos.LerAsync(cliente, primeira.Seguinte!.Texto);

        PaginaDeDistritos volta = await PaginaDeDistritos.LerAsync(cliente, segunda.Anterior!.Texto);

        segunda.Ids.Should().NotIntersectWith(primeira.Ids);
        volta.Ids.Should().Equal(primeira.Ids);
        volta.Anterior.Should().BeNull();
    }

    [Fact(DisplayName = "Seguir o next percorre todos os distritos, sem repetir nem saltar, e a última página não tem next")]
    public async Task Next_PercorreTodosOsDistritos_SemRepetir()
    {
        await _cenario.SemearAsync(5);
        using HttpClient cliente = _fixture.Factory.CreateAuthenticatedClient();

        List<PaginaDeDistritos> paginas = await PercorrerParaAFrenteAsync(cliente);

        paginas.Select(pagina => pagina.Ids.Count).Should().Equal(2, 2, 1);
        paginas.SelectMany(pagina => pagina.Ids).Should().OnlyHaveUniqueItems().And.HaveCount(5);
        paginas[^1].Seguinte.Should().BeNull();
    }

    [Fact(DisplayName = "Seguir o prev a partir da última página reentrega as páginas anteriores, em ordem, até a primeira")]
    public async Task Prev_PercorreAsPaginasAnteriores_AteAPrimeira()
    {
        await _cenario.SemearAsync(5);
        using HttpClient cliente = _fixture.Factory.CreateAuthenticatedClient();
        List<PaginaDeDistritos> paraAFrente = await PercorrerParaAFrenteAsync(cliente);

        PaginaDeDistritos penultima = await PaginaDeDistritos.LerAsync(cliente, paraAFrente[2].Anterior!.Texto);
        PaginaDeDistritos primeira = await PaginaDeDistritos.LerAsync(cliente, penultima.Anterior!.Texto);

        penultima.Ids.Should().Equal(paraAFrente[1].Ids);
        primeira.Ids.Should().Equal(paraAFrente[0].Ids);
        primeira.Anterior.Should().BeNull();
        primeira.Seguinte.Should().NotBeNull();
    }

    [Theory(DisplayName = "Com exatamente o limite de itens não há next; com um item a mais, há next e a página seguinte tem o item que sobrou")]
    [InlineData(5, false)]
    [InlineData(4, true)]
    public async Task Limite_NaBordaExata_DecideSeHaNext(int limite, bool haNext)
    {
        await _cenario.SemearAsync(5);
        using HttpClient cliente = _fixture.Factory.CreateAuthenticatedClient();

        PaginaDeDistritos pagina = await PaginaDeDistritos.LerAsync(cliente, $"{Distritos}?limit={limite}");

        pagina.Ids.Should().HaveCount(limite);
        (pagina.Seguinte is not null).Should().Be(haNext);
        pagina.Anterior.Should().BeNull();
    }

    [Fact(DisplayName = "A página seguinte à borda tem só o item que sobrou e aponta de volta com prev")]
    public async Task PaginaSeguinteAABorda_TemOItemQueSobrou()
    {
        await _cenario.SemearAsync(5);
        using HttpClient cliente = _fixture.Factory.CreateAuthenticatedClient();
        PaginaDeDistritos primeira = await PaginaDeDistritos.LerAsync(cliente, $"{Distritos}?limit=4");

        PaginaDeDistritos ultima = await PaginaDeDistritos.LerAsync(cliente, primeira.Seguinte!.Texto);

        ultima.Ids.Should().HaveCount(1).And.NotIntersectWith(primeira.Ids);
        ultima.Anterior.Should().NotBeNull();
        ultima.Seguinte.Should().BeNull();
    }

    private static async Task<List<PaginaDeDistritos>> PercorrerParaAFrenteAsync(HttpClient cliente)
    {
        List<PaginaDeDistritos> paginas = [await PaginaDeDistritos.LerAsync(cliente, $"{Distritos}?limit=2")];
        while (paginas[^1].Seguinte is { } seguinte)
        {
            paginas.Add(await PaginaDeDistritos.LerAsync(cliente, seguinte.Texto));
        }

        return paginas;
    }
}
