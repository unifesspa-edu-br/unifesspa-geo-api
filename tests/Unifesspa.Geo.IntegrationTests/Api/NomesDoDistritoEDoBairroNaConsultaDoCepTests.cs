namespace Unifesspa.Geo.IntegrationTests.Api;

using System.Net;
using System.Net.Http;
using System.Text.Json;

using AwesomeAssertions;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Consulta de CEP que resolve por logradouro: os nomes do distrito e do bairro vigentes
/// acompanham o logradouro principal e cada alternativo, e só uma cidade vigente resolve.
/// A collection é serial; cada teste apaga e semeia os dados.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class NomesDoDistritoEDoBairroNaConsultaDoCepTests
{
    private readonly GeoPostgisFixture _fixture;
    private readonly CenarioDeMarabaParaAsConsultas _cenario;

    public NomesDoDistritoEDoBairroNaConsultaDoCepTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _cenario = new CenarioDeMarabaParaAsConsultas(fixture);
    }

    [Fact(DisplayName = "CEP com dois logradouros traz distrito e bairro no principal e no alternativo")]
    public async Task Cep_TrazDistritoEBairroDoPrincipalEDoAlternativo()
    {
        await _cenario.GravarAsync(AdicionarDoisLogradourosComDistritoEBairro);

        JsonElement raiz = await ResolverAsync("68501000");

        raiz.GetProperty("logradouro").GetString().Should().Be("Alameda das Palmeiras");
        raiz.GetProperty("distrito").GetString().Should().Be("Nova Marabá");
        raiz.GetProperty("bairro").GetString().Should().Be("Cidade Nova");
        JsonElement alternativo = raiz.GetProperty("alternativos").EnumerateArray().Single();
        alternativo.GetProperty("distrito").GetString().Should().Be("Morada Nova");
        alternativo.GetProperty("bairro").GetString().Should().Be("Folha 31");
    }

    [Fact(DisplayName = "Bairro ou distrito não vigente sai nulo, no principal e nos alternativos; sem vínculo também")]
    public async Task Cep_BairroEDistritoNaoVigentesSaemNulos()
    {
        await _cenario.GravarAsync(AdicionarLogradourosComBairroEDistritoNaoVigentes);

        JsonElement raiz = await ResolverAsync("68502000");

        raiz.GetProperty("logradouro").GetString().Should().Be("Rua Alfa");
        raiz.GetProperty("distrito").GetString().Should().Be("Nova Marabá");
        raiz.GetProperty("bairro").GetString().Should().BeNull("o bairro do principal não está vigente");
        JsonElement[] alternativos = [.. raiz.GetProperty("alternativos").EnumerateArray()];
        alternativos.Select(a => a.GetProperty("logradouro").GetString()).Should().Equal("Rua Beta", "Rua Gama");
        ConferirNomes(alternativos[0], distrito: null, bairro: "Cidade Nova");
        ConferirNomes(alternativos[1], distrito: null, bairro: null);
    }

    [Fact(DisplayName = "Logradouro de cidade não vigente não resolve o CEP")]
    public async Task Cep_LogradouroDeCidadeNaoVigente_NaoResolve()
    {
        await _cenario.GravarComCidadeForaDeVigenciaAsync(AdicionarLogradouroSemDistritoNemBairro);
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();

        using HttpResponseMessage resposta = await GeoReferenceSeed.Obter(client, "/api/cep/68503000");

        resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static void ConferirNomes(JsonElement alternativo, string? distrito, string? bairro)
    {
        alternativo.GetProperty("distrito").GetString().Should().Be(distrito);
        alternativo.GetProperty("bairro").GetString().Should().Be(bairro);
    }

    private async Task<JsonElement> ResolverAsync(string cep)
    {
        using HttpClient client = _fixture.Factory.CreateAuthenticatedClient();
        return await RespostaDoCep.ResolverAsync(client, $"/api/cep/{cep}");
    }

    // Desempate por nome normalizado: "alameda das palmeiras" vem antes de "avenida tocantins".
    private static void AdicionarDoisLogradourosComDistritoEBairro(GeoDbContext ctx, Cidade cidade)
    {
        Distrito novaMaraba = GeoReferenceSeed.NovoDistrito(cidade.Id, "PA", "Nova Marabá");
        Distrito moradaNova = GeoReferenceSeed.NovoDistrito(cidade.Id, "PA", "Morada Nova");
        Bairro cidadeNova = GeoReferenceSeed.NovoBairro(cidade.Id, "PA", "Cidade Nova");
        Bairro folha31 = GeoReferenceSeed.NovoBairro(cidade.Id, "PA", "Folha 31");
        ctx.Distritos.AddRange(novaMaraba, moradaNova);
        ctx.Bairros.AddRange(cidadeNova, folha31);
        ctx.Logradouros.AddRange(
            CenarioDeMarabaParaAsConsultas.LogradouroEm(cidade, "68501000", "Alameda das Palmeiras", "Alameda", novaMaraba, cidadeNova),
            CenarioDeMarabaParaAsConsultas.LogradouroEm(cidade, "68501000", "Avenida Tocantins", "Avenida", moradaNova, folha31));
    }

    // Rua Alfa: distrito vigente e bairro não vigente. Rua Beta: distrito não vigente e bairro
    // vigente. Rua Gama: sem distrito e sem bairro.
    private static void AdicionarLogradourosComBairroEDistritoNaoVigentes(GeoDbContext ctx, Cidade cidade)
    {
        Distrito vigente = GeoReferenceSeed.NovoDistrito(cidade.Id, "PA", "Nova Marabá");
        Distrito antigo = GeoReferenceSeed.NovoDistrito(cidade.Id, "PA", "Distrito Antigo", vigente: false);
        Bairro cidadeNova = GeoReferenceSeed.NovoBairro(cidade.Id, "PA", "Cidade Nova");
        Bairro bairroAntigo = GeoReferenceSeed.NovoBairro(cidade.Id, "PA", "Bairro Antigo", vigente: false);
        ctx.Distritos.AddRange(vigente, antigo);
        ctx.Bairros.AddRange(cidadeNova, bairroAntigo);
        ctx.Logradouros.AddRange(
            CenarioDeMarabaParaAsConsultas.LogradouroEm(cidade, "68502000", "Rua Alfa", "Rua", vigente, bairroAntigo),
            CenarioDeMarabaParaAsConsultas.LogradouroEm(cidade, "68502000", "Rua Beta", "Rua", antigo, cidadeNova),
            CenarioDeMarabaParaAsConsultas.LogradouroEm(cidade, "68502000", "Rua Gama", "Rua"));
    }

    private static void AdicionarLogradouroSemDistritoNemBairro(GeoDbContext ctx, Cidade cidade) =>
        ctx.Logradouros.Add(CenarioDeMarabaParaAsConsultas.LogradouroEm(cidade, "68503000", "Rua Delta", "Rua"));
}
