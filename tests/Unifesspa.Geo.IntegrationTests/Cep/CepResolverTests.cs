namespace Unifesspa.Geo.IntegrationTests.Cep;

using AwesomeAssertions;

using NSubstitute;

using Unifesspa.Geo.Application.DTOs;
using Unifesspa.Geo.Infrastructure.Caching;
using Unifesspa.Geo.Infrastructure.Cep;

/// <summary>
/// Testes de unidade do cache-aside do <see cref="CepResolver"/> (#676) — sem
/// Redis/DB reais (substitutos NSubstitute). Provam o contrato que a integração não
/// consegue afirmar com precisão: hit não toca o reader (CA-07), 404 nunca é
/// cacheado (CA-08), a chave compõe-se com o selo de versão, o lookup degrada
/// para o banco quando o Redis está fora, e a memoização do selo (#703) poupa
/// round-trips ao Redis dentro do TTL.
/// </summary>
public sealed class CepResolverTests
{
    private const string Selo = "202601";
    private const string Cep = "01001000";
    private const string ChaveCep = $"geo:cep:f2:v{Selo}:{Cep}";

    private static CepResolvidoDto Endereco() => new(
        Cep, "Praça", "Praça da Sé", null, "Sé", null, "São Paulo", "3550308", "SP",
        -23.55m, -46.63m, CepResolucao.NivelLogradouro, CepResolucao.OrigemLogradouro);

    [Fact(DisplayName = "CA-07: cache hit devolve do Redis e não consulta o reader (banco)")]
    public async Task CacheHit_NaoConsultaReader()
    {
        using CenarioDoResolvedorDeCep cenario = new();
        CepResolvidoDto cacheado = Endereco();
        cenario.ComSeloVigente(Selo);
        cenario.ComEntradaNoCache(ChaveCep, cacheado);

        CepResolvidoDto? resultado = await cenario.NovoResolver().ResolverAsync(Cep, CancellationToken.None);

        resultado.Should().Be(cacheado);
        await cenario.Reader.DidNotReceive().ResolverAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Cache miss: resolve do banco e popula a chave versionada com o TTL")]
    public async Task CacheMiss_ResolveDoBancoEPopula()
    {
        using CenarioDoResolvedorDeCep cenario = new();
        CepResolvidoDto resolvido = Endereco();
        cenario.ComSeloVigente(Selo);
        cenario.ComEntradaNoCache(ChaveCep, (CepResolvidoDto?)null);
        cenario.ComResolucaoNoBanco(Cep, resolvido);

        CepResolvidoDto? resultado = await cenario.NovoResolver().ResolverAsync(Cep, CancellationToken.None);

        resultado.Should().Be(resolvido);
        await cenario.Reader.Received(1).ResolverAsync(Cep, Arg.Any<CancellationToken>());
        await cenario.Cache.Received(1).DefinirAsync(ChaveCep, resolvido, Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Entrada gravada no formato anterior (mesmo selo) é ignorada: o valor novo não fica preso no cache antigo")]
    public async Task EntradaDoFormatoAnterior_NaoEDevolvida()
    {
        using CenarioDoResolvedorDeCep cenario = new();
        CepResolvidoDto antigo = Endereco() with { Origem = CepResolucao.OrigemFaixaCidade };
        CepResolvidoDto atual = Endereco() with { Origem = CepResolucao.OrigemCepGeral };
        cenario.ComSeloVigente(Selo);
        cenario.ComEntradaNoCache($"geo:cep:v{Selo}:{Cep}", antigo);
        cenario.ComResolucaoNoBanco(Cep, atual);

        CepResolvidoDto? resultado = await cenario.NovoResolver().ResolverAsync(Cep, CancellationToken.None);

        resultado!.Origem.Should().Be(CepResolucao.OrigemCepGeral);
    }

    [Fact(DisplayName = "CA-08: reader devolve null (404) — nada é gravado no cache")]
    public async Task ReaderNull_NaoCacheia()
    {
        using CenarioDoResolvedorDeCep cenario = new();
        cenario.ComSeloVigente(Selo);
        cenario.ComEntradaNoCache(ChaveCep, (CepResolvidoDto?)null);
        cenario.ComResolucaoNoBanco(Cep, (CepResolvidoDto?)null);

        CepResolvidoDto? resultado = await cenario.NovoResolver().ResolverAsync(Cep, CancellationToken.None);

        resultado.Should().BeNull();
        await cenario.Cache.DidNotReceive().DefinirAsync(Arg.Any<string>(), Arg.Any<CepResolvidoDto>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Redis indisponível (Lazy lança ao conectar): degrada para o banco sem propagar erro")]
    public async Task CacheIndisponivel_DegradaParaBanco()
    {
        using CenarioDoResolvedorDeCep cenario = new();
        CepResolvidoDto resolvido = Endereco();
        cenario.ComResolucaoNoBanco(Cep, resolvido);

        CepResolvidoDto? resultado = await cenario.NovoResolverComRedisForaDoAr().ResolverAsync(Cep, CancellationToken.None);

        resultado.Should().Be(resolvido);
        await cenario.Reader.Received(1).ResolverAsync(Cep, Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Sem selo de versão (ETL ainda não selou): resolve do banco e não cacheia")]
    public async Task SemSelo_NaoCacheia()
    {
        using CenarioDoResolvedorDeCep cenario = new();
        cenario.ComSeloVigente((string?)null);
        CepResolvidoDto resolvido = Endereco();
        cenario.ComResolucaoNoBanco(Cep, resolvido);

        CepResolvidoDto? resultado = await cenario.NovoResolver().ResolverAsync(Cep, CancellationToken.None);

        resultado.Should().Be(resolvido);
        await cenario.Reader.Received(1).ResolverAsync(Cep, Arg.Any<CancellationToken>());
        await cenario.Cache.DidNotReceive().ObterAsync<CepResolvidoDto>(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await cenario.Cache.DidNotReceive().DefinirAsync(Arg.Any<string>(), Arg.Any<CepResolvidoDto>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "#703: 2º lookup dentro do TTL não relê o selo do Redis (memoização em processo)")]
    public async Task SeloMemoizado_NaoReleDentroDoTtl()
    {
        using CenarioDoResolvedorDeCep cenario = new();
        MutableTimeProvider relogio = new();
        cenario.ComSeloVigente(Selo);
        cenario.ComEntradaNoCache(ChaveCep, Endereco());

        // Dois resolvers compartilhando o IMemoryCache singleton (topologia produtiva:
        // resolver scoped por request, cache em memória singleton).
        await cenario.NovoResolver(relogio).ResolverAsync(Cep, CancellationToken.None);
        await cenario.NovoResolver(relogio).ResolverAsync(Cep, CancellationToken.None);

        await cenario.Cache.Received(1)
            .ObterAsync<string>(RedisGeoCepCacheInvalidador.ChaveSeloVersaoVigente, Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "#703: após a expiração do TTL o selo é relido do Redis")]
    public async Task SeloMemoizado_ReleAposExpiracao()
    {
        using CenarioDoResolvedorDeCep cenario = new();
        MutableTimeProvider relogio = new();
        GeoCepCacheOptions opcoes = new() { SeloTtl = TimeSpan.FromSeconds(15) };
        cenario.ComSeloVigente(Selo);
        cenario.ComEntradaNoCache(ChaveCep, Endereco());

        await cenario.NovoResolver(relogio, opcoes).ResolverAsync(Cep, CancellationToken.None);
        relogio.Advance(TimeSpan.FromSeconds(20));
        await cenario.NovoResolver(relogio, opcoes).ResolverAsync(Cep, CancellationToken.None);

        await cenario.Cache.Received(2)
            .ObterAsync<string>(RedisGeoCepCacheInvalidador.ChaveSeloVersaoVigente, Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "#703: SeloTtl = Zero desliga a memoização (relê a cada request)")]
    public async Task SeloTtlZero_DesligaMemoizacao()
    {
        using CenarioDoResolvedorDeCep cenario = new();
        GeoCepCacheOptions opcoes = new() { SeloTtl = TimeSpan.Zero };
        cenario.ComSeloVigente(Selo);
        cenario.ComEntradaNoCache(ChaveCep, Endereco());

        await cenario.NovoResolver(opcoes: opcoes).ResolverAsync(Cep, CancellationToken.None);
        await cenario.NovoResolver(opcoes: opcoes).ResolverAsync(Cep, CancellationToken.None);

        await cenario.Cache.Received(2)
            .ObterAsync<string>(RedisGeoCepCacheInvalidador.ChaveSeloVersaoVigente, Arg.Any<CancellationToken>());
    }

    // TimeProvider mutável para exercitar a expiração do TTL de memoização sem esperar
    // tempo real (espelha o helper de CursorEncoderTests).
    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 6, 19, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan span) => _now = _now.Add(span);
    }
}
