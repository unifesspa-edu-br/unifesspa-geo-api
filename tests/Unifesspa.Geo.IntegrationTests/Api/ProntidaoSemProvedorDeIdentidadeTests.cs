namespace Unifesspa.Geo.IntegrationTests.Api;

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.Json;

using AwesomeAssertions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using NSubstitute;

using StackExchange.Redis;

using Unifesspa.Geo.Infrastructure.Core.Authentication;
using Unifesspa.Geo.Infrastructure.Core.DependencyInjection;
using Unifesspa.Geo.Infrastructure.Core.HealthChecks;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// O provedor de identidade só afeta as rotas autenticadas, que já recusam a requisição sozinhas;
/// retirá-lo da prontidão mantém no ar as rotas públicas, e o estado dele segue visível no health completo.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit exige classe de teste pública.")]
public sealed class ProntidaoSemProvedorDeIdentidadeTests : IClassFixture<GeoProntidaoApiFactory>
{
    private const string VerificacaoDoProvedor = "oidc-discovery";

    private readonly GeoProntidaoApiFactory _factory;

    public ProntidaoSemProvedorDeIdentidadeTests(GeoProntidaoApiFactory factory)
    {
        _factory = factory;
    }

    [Fact(DisplayName = "Provedor de identidade fora do ar não reprova a prontidão")]
    public async Task Prontidao_ProvedorDeIdentidadeForaDoAr_ContinuaApta()
    {
        _factory.ProvedorDeIdentidade.Status = HttpStatusCode.ServiceUnavailable;
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage resposta = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact(DisplayName = "Dependência de prontidão fora do ar reprova a prontidão mesmo com o provedor de identidade no ar")]
    public async Task Prontidao_DependenciaDeProntidaoForaDoAr_Reprova()
    {
        _factory.ProvedorDeIdentidade.Status = HttpStatusCode.OK;
        _factory.DependenciaDeProntidao.Disponivel = false;
        try
        {
            using HttpClient client = _factory.CreateClient();

            using HttpResponseMessage resposta = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

            resposta.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        }
        finally
        {
            _factory.DependenciaDeProntidao.Disponivel = true;
        }
    }

    [Fact(DisplayName = "Provedor de identidade fora do ar aparece como degradado no health completo")]
    public async Task HealthCompleto_ProvedorDeIdentidadeForaDoAr_ReportaDegradado()
    {
        _factory.ProvedorDeIdentidade.Status = HttpStatusCode.ServiceUnavailable;
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage resposta = await client.GetAsync(new Uri("/health", UriKind.Relative));

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        corpo.RootElement.GetProperty("verificacoes").GetProperty(VerificacaoDoProvedor).GetProperty("status")
            .GetString().Should().Be("Degraded");
    }

    [Fact(DisplayName = "Provedor de identidade que responde aparece como saudável no health completo")]
    public async Task HealthCompleto_ProvedorDeIdentidadeRespondendo_ReportaSaudavel()
    {
        _factory.ProvedorDeIdentidade.Status = HttpStatusCode.OK;
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage resposta = await client.GetAsync(new Uri("/health", UriKind.Relative));

        using JsonDocument corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        corpo.RootElement.GetProperty("verificacoes").GetProperty(VerificacaoDoProvedor).GetProperty("status")
            .GetString().Should().Be("Healthy");
    }

    [Fact(DisplayName = "Provedor de identidade sem resposta degrada o health completo dentro do limite de espera")]
    public async Task HealthCompleto_ProvedorDeIdentidadeSemResposta_DegradaDentroDoLimite()
    {
        _factory.ProvedorDeIdentidade.Pendurado = true;
        try
        {
            HealthCheckService servico = _factory.Services.GetRequiredService<HealthCheckService>();
            Stopwatch cronometro = Stopwatch.StartNew();

            using CancellationTokenSource prazoExterno = new(TimeSpan.FromSeconds(10));

            HealthReport relatorio = await servico.CheckHealthAsync(
                r => r.Name == VerificacaoDoProvedor,
                prazoExterno.Token);

            relatorio.Entries[VerificacaoDoProvedor].Status.Should().Be(HealthStatus.Degraded);
            cronometro.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3.5));
        }
        finally
        {
            _factory.ProvedorDeIdentidade.Pendurado = false;
        }
    }

    [Fact(DisplayName = "O HttpClient da verificação do provedor de identidade espera no máximo 2 segundos")]
    public void VerificacaoDoProvedor_HttpClient_LimitaAEsperaADoisSegundos()
    {
        using HttpClient cliente = _factory.Services.GetRequiredService<IHttpClientFactory>()
            .CreateClient(nameof(OidcDiscoveryHealthCheck));

        cliente.Timeout.Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact(DisplayName = "A verificação do provedor de identidade não integra a prontidão")]
    public void Prontidao_Inventario_NaoContemOProvedorDeIdentidade()
    {
        HealthCheckServiceOptions opcoes = _factory.Services
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;

        opcoes.Registrations
            .Where(r => r.Tags.Contains(HealthChecksServiceCollectionExtensions.ReadyTag))
            .Select(r => r.Name)
            .Should().BeEquivalentTo([GeoProntidaoApiFactory.VerificacaoDeProntidao]);
        opcoes.Registrations.Select(r => r.Name).Should().Contain(VerificacaoDoProvedor);
    }

    [Fact(DisplayName = "Postgres e Redis seguem na prontidão e o OIDC fica de fora quando todas as dependências estão configuradas")]
    public void Prontidao_RegistrosProdutivos_MantemPostgresERedisSemOProvedor()
    {
        IConfiguration configuracao = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:GeoDb"] = "Host=localhost;Database=geo",
                ["Redis:ConnectionString"] = "localhost:6379",
                ["Auth:Authority"] = "http://provedor.invalido/test-realm",
                ["Auth:Audience"] = "uniplus",
            })
            .Build();
        ServiceCollection servicos = new();
        servicos.AddSingleton(Substitute.For<IConnectionMultiplexer>());
        servicos.AddOidcAuthentication(configuracao, Substitute.For<IHostEnvironment>());
        servicos.AddGeoHealthChecks(configuracao, "GeoDb");
        using ServiceProvider provedor = servicos.BuildServiceProvider();

        HealthCheckServiceOptions opcoes = provedor.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;

        opcoes.Registrations
            .Where(r => r.Tags.Contains(HealthChecksServiceCollectionExtensions.ReadyTag))
            .Select(r => r.Name)
            .Should().BeEquivalentTo(["postgres", "redis"]);
        opcoes.Registrations.Select(r => r.Name).Should().Contain(VerificacaoDoProvedor);
    }
}
