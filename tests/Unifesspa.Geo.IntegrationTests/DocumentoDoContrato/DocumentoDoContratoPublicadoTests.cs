namespace Unifesspa.Geo.IntegrationTests.DocumentoDoContrato;

using AwesomeAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Unifesspa.Geo.Infrastructure.Core.DependencyInjection;
using Unifesspa.Geo.Infrastructure.Core.OpenApi;

/// <summary>
/// A restrição de rota só deixa passar o nome de um documento que o serviço publica, escrito
/// exatamente como foi publicado.
/// </summary>
public sealed class DocumentoDoContratoPublicadoTests
{
    private readonly DocumentoDoContratoPublicado _restricao = new();

    [Fact(DisplayName = "Nome de documento publicado é aceito")]
    public void NomePublicado_EhAceito() =>
        Confirmar("geo").Should().BeTrue();

    [Fact(DisplayName = "Nome de documento que o serviço não publica é recusado")]
    public void NomeNaoPublicado_EhRecusado() =>
        Confirmar("inexistente").Should().BeFalse();

    [Fact(DisplayName = "Nome publicado escrito com outra grafia é recusado")]
    public void NomePublicadoComOutraGrafia_EhRecusado() =>
        Confirmar("GEO").Should().BeFalse();

    [Fact(DisplayName = "Pedido sem contexto HTTP é recusado")]
    public void SemContextoHttp_EhRecusado()
    {
        RouteValueDictionary valores = new() { ["documentName"] = "geo" };

        bool aceito = _restricao.Match(httpContext: null, route: null, "documentName", valores, RouteDirection.IncomingRequest);

        aceito.Should().BeFalse();
    }

    private bool Confirmar(string nome)
    {
        ServiceCollection servicos = new();
        servicos.AddGeoOpenApi("geo", new ConfigurationBuilder().Build());
        using ServiceProvider provedor = servicos.BuildServiceProvider();
        DefaultHttpContext contexto = new() { RequestServices = provedor };
        RouteValueDictionary valores = new() { ["documentName"] = nome };

        return _restricao.Match(contexto, route: null, "documentName", valores, RouteDirection.IncomingRequest);
    }
}
