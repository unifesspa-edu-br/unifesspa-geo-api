namespace Unifesspa.Geo.IntegrationTests.Registro;

using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

/// <summary>Ambiente de execução fictício, para registrar serviços que dependem do nome do ambiente.</summary>
internal sealed class AmbienteDeTeste : IHostEnvironment
{
    public AmbienteDeTeste(string nome)
    {
        EnvironmentName = nome;
    }

    public string EnvironmentName { get; set; }

    public string ApplicationName { get; set; } = "Geo.Testes";

    public string ContentRootPath { get; set; } = "/";

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
