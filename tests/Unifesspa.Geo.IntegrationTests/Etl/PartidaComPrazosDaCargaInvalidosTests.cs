namespace Unifesspa.Geo.IntegrationTests.Etl;

using AwesomeAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Unifesspa.Geo.Infrastructure.Persistence.Etl;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>O serviço não inicia com os prazos da carga fora da faixa e inicia com os padrões.</summary>
public sealed class PartidaComPrazosDaCargaInvalidosTests : IClassFixture<GeoOpenApiFactory>
{
    private readonly GeoOpenApiFactory _factory;

    public PartidaComPrazosDaCargaInvalidosTests(GeoOpenApiFactory factory)
    {
        _factory = factory;
    }

    [Fact(DisplayName = "Partida com a duração máxima zerada falha dizendo a opção e a faixa aceita")]
    public void DuracaoZerada_ImpedeAPartida()
    {
        string mensagem = MensagemDaPartidaRecusada("Geo:Etl:DuracaoMaximaDaCarga", "00:00:00");

        mensagem.Should().Contain("Geo:Etl:DuracaoMaximaDaCarga deve ser maior que zero e no máximo 49 dias.");
        mensagem.Should().NotContain("Host=");
    }

    [Fact(DisplayName = "Partida com o tempo limite dos comandos negativo falha dizendo a opção e a faixa aceita")]
    public void TempoLimiteNegativo_ImpedeAPartida()
    {
        string mensagem = MensagemDaPartidaRecusada("Geo:Etl:TempoLimiteDosComandosDaCarga", "-00:00:05");

        mensagem.Should().Contain("Geo:Etl:TempoLimiteDosComandosDaCarga deve ser maior que zero e no máximo 2147483 segundos.");
        mensagem.Should().NotContain("Host=");
    }

    [Fact(DisplayName = "Partida com prazos válidos é concluída")]
    public void PrazosValidos_PermitemAPartida()
    {
        using WebApplicationFactory<Program> valido = ComOpcao("Geo:Etl:DuracaoMaximaDaCarga", "02:00:00");

        Action iniciar = () => valido.CreateClient().Dispose();

        iniciar.Should().NotThrow();
    }

    [Fact(DisplayName = "Partida sem os prazos informados é concluída com os padrões de 1 hora e 30 minutos")]
    public void SemPrazos_ValemOsPadroes()
    {
        using HttpClient cliente = _factory.CreateClient();

        EtlOpcoes opcoes = _factory.Services.GetRequiredService<IOptions<EtlOpcoes>>().Value;
        opcoes.DuracaoMaximaDaCarga.Should().Be(TimeSpan.FromHours(1));
        opcoes.TempoLimiteDosComandosDaCarga.Should().Be(TimeSpan.FromMinutes(30));
    }

    private string MensagemDaPartidaRecusada(string opcao, string valor)
    {
        using WebApplicationFactory<Program> invalido = ComOpcao(opcao, valor);

        Action iniciar = () => invalido.CreateClient().Dispose();

        return iniciar.Should().Throw<OptionsValidationException>().Which.Message;
    }

    private WebApplicationFactory<Program> ComOpcao(string opcao, string valor) =>
        _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuracao) =>
                configuracao.AddInMemoryCollection([new KeyValuePair<string, string?>(opcao, valor)])));
}
