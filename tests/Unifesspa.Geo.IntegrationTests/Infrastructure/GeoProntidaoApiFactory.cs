namespace Unifesspa.Geo.IntegrationTests.Infrastructure;

using System.Diagnostics.CodeAnalysis;
using System.Net;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using Unifesspa.Geo.Infrastructure.Core.HealthChecks;
using Unifesspa.Geo.IntegrationTests.Fixtures.Hosting;

/// <summary>
/// Factory que mantém a verificação <c>oidc-discovery</c> ativa e a aponta para um provedor de identidade
/// simulado, para exercitar a prontidão com o provedor inacessível. Postgres e Redis saem do conjunto
/// (default do <see cref="ApiFactoryBase{T}"/>): o que está em prova é só o comportamento do OIDC.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit IClassFixture<T> exige tipo público para a fixture compartilhada.")]
public sealed class GeoProntidaoApiFactory : ApiFactoryBase<Program>
{
    public ProvedorDeIdentidadeSimulado ProvedorDeIdentidade { get; } = new();

    protected override IEnumerable<KeyValuePair<string, string?>> GetConfigurationOverrides() =>
    [
        new("ConnectionStrings:GeoDb", "Host=localhost;Port=5432;Database=uniplus_tests;Username=uniplus;Password=uniplus_dev"),
        new("Auth:Authority", "http://provedor.invalido/test-realm"),
        new("Auth:Audience", "uniplus"),
        new("Geo:Etl:WorkerHabilitado", "false"),
        new("Redis:ConnectionString", string.Empty),
    ];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
            services.AddHttpClient(nameof(OidcDiscoveryHealthCheck))
                .ConfigurePrimaryHttpMessageHandler(() => new RespostaDoProvedorHandler(ProvedorDeIdentidade)));
    }

    private sealed class RespostaDoProvedorHandler(ProvedorDeIdentidadeSimulado provedor) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (provedor.Pendurado)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return new HttpResponseMessage(provedor.Status);
        }
    }
}

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "Exposto pela fixture pública.")]
public sealed class ProvedorDeIdentidadeSimulado
{
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

    public bool Pendurado { get; set; }
}
