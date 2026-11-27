namespace Unifesspa.Geo.IntegrationTests.Errors;

using AwesomeAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>O serviço não inicia sem a base do endereço dos erros.</summary>
public sealed class PartidaSemBaseDoEnderecoDosErrosTests : IClassFixture<GeoOpenApiFactory>
{
    private readonly GeoOpenApiFactory _factory;

    public PartidaSemBaseDoEnderecoDosErrosTests(GeoOpenApiFactory factory)
    {
        _factory = factory;
    }

    [Fact(DisplayName = "Partida com a base do endereço dos erros vazia falha dizendo o que corrigir")]
    public void BaseVazia_ImpedeAPartida()
    {
        using WebApplicationFactory<Program> semBase = _factory.WithWebHostBuilder(static builder =>
            builder.ConfigureAppConfiguration(static (_, configuracao) =>
                configuracao.AddInMemoryCollection([new KeyValuePair<string, string?>("ProblemType:BaseUri", string.Empty)])));

        Action iniciar = () => semBase.CreateClient().Dispose();

        iniciar.Should().Throw<OptionsValidationException>().Which.Message.Should().Contain("ProblemType:BaseUri");
    }
}
