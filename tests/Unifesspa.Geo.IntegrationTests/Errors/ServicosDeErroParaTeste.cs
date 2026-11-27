namespace Unifesspa.Geo.IntegrationTests.Errors;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Unifesspa.Geo.Infrastructure.Core.DependencyInjection;

/// <summary>Monta os serviços de erro reais do Geo a partir de uma base de endereço informada.</summary>
internal static class ServicosDeErroParaTeste
{
    internal const string BaseDoPortal = "https://portal.exemplo.test/erros/";

    public static ServiceProvider Criar(string? baseDoEndereco = BaseDoPortal)
    {
        IConfiguration configuracao = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("ProblemType:BaseUri", baseDoEndereco)])
            .Build();

        ServiceCollection servicos = new();
        servicos.AddDomainErrorMapper(configuracao);
        return servicos.BuildServiceProvider(validateScopes: true);
    }
}
