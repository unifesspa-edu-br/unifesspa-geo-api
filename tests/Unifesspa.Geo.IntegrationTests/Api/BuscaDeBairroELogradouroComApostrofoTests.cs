namespace Unifesspa.Geo.IntegrationTests.Api;

using System.Net.Http;

using AwesomeAssertions;

using Microsoft.EntityFrameworkCore;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.IntegrationTests.Etl;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// A busca de município ignora o apóstrofo, mas a de bairro e de logradouro segue o texto
/// sem acento da DNE, que mantém o apóstrofo (<c>´</c> e <c>`</c>): digitar o termo como a
/// DNE o escreve continua achando o bairro e o logradouro.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class BuscaDeBairroELogradouroComApostrofoTests
{
    private const string CodigoSantaBarbara = FonteComMunicipiosDeApostrofo.SantaBarbaraDOeste;

    private readonly GeoPostgisFixture _fixture;

    public BuscaDeBairroELogradouroComApostrofoTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(DisplayName = "Busca de bairro com o acento agudo da DNE («caixa d´agua») acha o bairro «Caixa D´Agua»")]
    public async Task Bairro_BuscaComAcentoAgudo_AchaOBairro()
    {
        await SemearAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        List<string> nomes = await ListaDaApi.CamposAsync(
            client, $"/api/cidades/{CodigoSantaBarbara}/bairros?q={Uri.EscapeDataString("caixa d´agua")}&limit=100", "nome");

        nomes.Should().Equal("Caixa D´Agua");
    }

    [Fact(DisplayName = "Busca de logradouro com a crase da DNE («caixa d`agua») acha «Rua Caixa D`Agua»")]
    public async Task Logradouro_BuscaComCrase_AchaOLogradouro()
    {
        await SemearAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        List<string> nomes = await ListaDaApi.CamposAsync(
            client, $"/api/cidades/{CodigoSantaBarbara}/logradouros?q={Uri.EscapeDataString("caixa d`agua")}&limit=100", "nomeCompleto");

        nomes.Should().Equal("Rua Caixa D`Agua");
    }

    [Theory(DisplayName = "Busca de município «d'oeste» e «doeste» acha Santa Bárbara d'Oeste")]
    [InlineData("d'oeste")]
    [InlineData("doeste")]
    public async Task Municipio_BuscaComESemApostrofo_AchaSantaBarbaraDOeste(string termo)
    {
        await SemearAsync();
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        List<string> nomes = await ListaDaApi.CamposAsync(
            client, $"/api/cidades?q={Uri.EscapeDataString(termo)}&limit=100", "nome");

        nomes.Should().Equal("Santa Bárbara d'Oeste");
    }

    private async Task SemearAsync()
    {
        await GeoReferenceSeed.LimparAsync(_fixture);
        await CargaPelaEtl.ExecutarAsync(_fixture, FonteComMunicipiosDeApostrofo.Criar());
        await using GeoDbContext ctx = _fixture.CreateDbContext();
        Cidade cidade = await ctx.Cidades.SingleAsync(c => c.CodigoIbge == CodigoSantaBarbara);
        AdicionarBairrosELogradouros(ctx, cidade);
        await ctx.SaveChangesAsync();
    }

    private static void AdicionarBairrosELogradouros(GeoDbContext ctx, Cidade cidade)
    {
        Bairro caixaDAgua = GeoReferenceSeed.NovoBairro(cidade.Id, "SP", "Caixa D\u00B4Agua");
        Bairro centro = GeoReferenceSeed.NovoBairro(cidade.Id, "SP", "Centro");
        ctx.Bairros.AddRange(caixaDAgua, centro);
        ctx.Logradouros.Add(NovaRua(cidade, "13450000", "Caixa D`Agua", caixaDAgua));
        ctx.Logradouros.Add(NovaRua(cidade, "13450001", "das Flores", centro));
    }

    private static Logradouro NovaRua(Cidade cidade, string cep, string nome, Bairro bairro) =>
        new LogradouroBuilder(cidade.Id, "SP", cep, nome)
            .DoTipo("Rua").ComNomeCompleto($"Rua {nome}").NoBairro(bairro.Id).Construir();
}
