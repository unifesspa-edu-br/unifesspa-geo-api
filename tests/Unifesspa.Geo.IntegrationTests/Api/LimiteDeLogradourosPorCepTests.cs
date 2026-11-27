namespace Unifesspa.Geo.IntegrationTests.Api;

using AwesomeAssertions;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Unifesspa.Geo.Application.DTOs;
using Unifesspa.Geo.Infrastructure.Cep;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.Infrastructure.Persistence.Readers;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Limite de logradouros devolvidos por CEP na leitura do banco: acima do teto trunca e
/// registra aviso, abaixo do teto não, e teto menor que um vale como um. A collection é
/// serial; cada teste apaga e semeia os dados.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class LimiteDeLogradourosPorCepTests
{
    private readonly GeoPostgisFixture _fixture;
    private readonly CenarioDaConsultaDoCep _cenario;

    public LimiteDeLogradourosPorCepTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _cenario = new CenarioDaConsultaDoCep(fixture);
    }

    [Fact(DisplayName = "#705: fan-out acima do teto trunca os logradouros e loga aviso, sem erro")]
    public async Task Cep_FanOutAcimaDoTeto_TruncaELoga()
    {
        await _cenario.PrepararAsync();
        await using GeoDbContext ctx = _fixture.CreateDbContext();
        LoggerQueGuardaAsEntradas<CepReader> logger = new();
        // 01310100 tem 2 logradouros; teto 1 força truncamento determinístico.
        CepReader reader = NovoLeitor(ctx, 1, logger);

        CepResolvidoDto? resolvido = await reader.ResolverAsync("01310100", CancellationToken.None);

        resolvido.Should().NotBeNull();
        // Desempate estável: "alameda santos" < "avenida paulista" → primário Alameda Santos.
        resolvido!.Logradouro.Should().Be("Alameda Santos");
        resolvido.Alternativos.Should().BeEmpty("o teto 1 trunca o segundo logradouro");
        logger.Entradas.Should().ContainSingle(e => e.Level == LogLevel.Warning);
    }

    [Fact(DisplayName = "#705: fan-out abaixo do teto não trunca nem loga")]
    public async Task Cep_FanOutAbaixoDoTeto_NaoTrunca()
    {
        await _cenario.PrepararAsync();
        await using GeoDbContext ctx = _fixture.CreateDbContext();
        LoggerQueGuardaAsEntradas<CepReader> logger = new();
        CepReader reader = NovoLeitor(ctx, 50, logger);

        CepResolvidoDto? resolvido = await reader.ResolverAsync("01310100", CancellationToken.None);

        resolvido.Should().NotBeNull();
        resolvido!.Alternativos.Should().ContainSingle("os 2 logradouros cabem no teto");
        logger.Entradas.Should().NotContain(e => e.Level == LogLevel.Warning);
    }

    [Fact(DisplayName = "#705: teto < 1 é tratado como 1 (clamp defensivo), sem esvaziar o lookup")]
    public async Task Cep_TetoInvalido_ClampParaUm()
    {
        await _cenario.PrepararAsync();
        await using GeoDbContext ctx = _fixture.CreateDbContext();
        CepReader reader = NovoLeitor(ctx, 0, new LoggerQueGuardaAsEntradas<CepReader>());

        CepResolvidoDto? resolvido = await reader.ResolverAsync("01310100", CancellationToken.None);

        resolvido.Should().NotBeNull("teto 0 seria Take(0) → null; o clamp para 1 preserva a resolução");
        resolvido!.Logradouro.Should().Be("Alameda Santos");
    }

    private static CepReader NovoLeitor(GeoDbContext ctx, int maximoDeLogradouros, ILogger<CepReader> logger)
    {
        return new CepReader(
            ctx,
            Options.Create(new GeoCepLookupOptions { MaxLogradourosPorCep = maximoDeLogradouros }),
            logger);
    }

    // Guarda o nível e a mensagem de cada entrada para afirmar o aviso de truncamento.
    private sealed class LoggerQueGuardaAsEntradas<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entradas { get; } = [];

        IDisposable? ILogger.BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            Entradas.Add((logLevel, formatter(state, exception)));
        }
    }
}
