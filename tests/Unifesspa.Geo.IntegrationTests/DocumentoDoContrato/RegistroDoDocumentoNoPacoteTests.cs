namespace Unifesspa.Geo.IntegrationTests.DocumentoDoContrato;

using AwesomeAssertions;

using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Unifesspa.Geo.Infrastructure.Core.DependencyInjection;

/// <summary>
/// A restrição de rota depende de o pacote de OpenAPI (chamado por AddGeoOpenApi) registrar o provedor de cada documento
/// por chave igual ao nome do documento; uma mudança de versão que altere isso falha aqui.
/// </summary>
public sealed class RegistroDoDocumentoNoPacoteTests
{
    [Fact(DisplayName = "Documento registrado no pacote de OpenAPI é um serviço por chave igual ao nome")]
    public void DocumentoRegistrado_EhServicoPorChaveComONome()
    {
        ServiceCollection servicos = new();
        servicos.AddGeoOpenApi("geo", new ConfigurationBuilder().Build());
        using ServiceProvider provedor = servicos.BuildServiceProvider();
        IServiceProviderIsKeyedService consulta = provedor.GetRequiredService<IServiceProviderIsKeyedService>();

        consulta.IsKeyedService(typeof(IOpenApiDocumentProvider), "geo").Should().BeTrue();
        consulta.IsKeyedService(typeof(IOpenApiDocumentProvider), "inexistente").Should().BeFalse();
    }
}
