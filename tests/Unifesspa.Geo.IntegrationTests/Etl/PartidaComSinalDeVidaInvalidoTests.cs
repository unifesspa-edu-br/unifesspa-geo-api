namespace Unifesspa.Geo.IntegrationTests.Etl;

using AwesomeAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>O serviço não inicia com o intervalo do sinal de vida da trava fora da faixa.</summary>
public sealed class PartidaComSinalDeVidaInvalidoTests : IClassFixture<GeoOpenApiFactory>
{
    private readonly GeoOpenApiFactory _factory;

    public PartidaComSinalDeVidaInvalidoTests(GeoOpenApiFactory factory)
    {
        _factory = factory;
    }

    [Fact(DisplayName = "Partida com o intervalo do sinal de vida zerado falha dizendo qual opção corrigir")]
    public void IntervaloZerado_ImpedeAPartida()
    {
        using WebApplicationFactory<Program> invalido = _factory.WithWebHostBuilder(static builder =>
            builder.ConfigureAppConfiguration(static (_, configuracao) =>
                configuracao.AddInMemoryCollection(
                    [new KeyValuePair<string, string?>("Geo:Etl:IntervaloDoSinalDeVidaDaTrava", "00:00:00")])));

        Action iniciar = () => invalido.CreateClient().Dispose();

        iniciar.Should().Throw<OptionsValidationException>().Which.Message
            .Should().Contain("Geo:Etl:IntervaloDoSinalDeVidaDaTrava");
    }
}
