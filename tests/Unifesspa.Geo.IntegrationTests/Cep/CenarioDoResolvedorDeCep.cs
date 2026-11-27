namespace Unifesspa.Geo.IntegrationTests.Cep;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NSubstitute;

using Unifesspa.Geo.Application.Abstractions;
using Unifesspa.Geo.Application.DTOs;
using Unifesspa.Geo.Infrastructure.Caching;
using Unifesspa.Geo.Infrastructure.Cep;
using Unifesspa.Geo.Infrastructure.Core.Caching;

/// <summary>
/// Apoio dos testes do <see cref="CepResolver"/>: substitutos do cache, do leitor do banco e da memória
/// em processo, com atalhos para programar o selo de versão e as entradas do cache. Usado por composição.
/// </summary>
internal sealed class CenarioDoResolvedorDeCep : IDisposable
{
    public ICacheService Cache { get; } = Substitute.For<ICacheService>();

    public ICepReader Reader { get; } = Substitute.For<ICepReader>();

    public MemoryCache Memoria { get; } = new(new MemoryCacheOptions());

    public void ComSeloVigente(string? selo) =>
        Cache.ObterAsync<string>(RedisGeoCepCacheInvalidador.ChaveSeloVersaoVigente, Arg.Any<CancellationToken>())
            .Returns(selo);

    public void ComEntradaNoCache(string chave, CepResolvidoDto? entrada) =>
        Cache.ObterAsync<CepResolvidoDto>(chave, Arg.Any<CancellationToken>())
            .Returns(entrada);

    public void ComResolucaoNoBanco(string cep, CepResolvidoDto? resolvido) =>
        Reader.ResolverAsync(cep, Arg.Any<CancellationToken>()).Returns(resolvido);

    public CepResolver NovoResolver(TimeProvider? relogio = null, GeoCepCacheOptions? opcoes = null) =>
        Criar(new Lazy<ICacheService>(() => Cache), relogio, opcoes);

    public CepResolver NovoResolverComRedisForaDoAr()
    {
        Lazy<ICacheService> cacheQuebrado = new(() => throw new InvalidOperationException("Redis fora do ar"));
        return Criar(cacheQuebrado, relogio: null, opcoes: null);
    }

    public void Dispose() => Memoria.Dispose();

    private CepResolver Criar(Lazy<ICacheService> cache, TimeProvider? relogio, GeoCepCacheOptions? opcoes) =>
        new(
            cache,
            Reader,
            Memoria,
            relogio ?? TimeProvider.System,
            Options.Create(opcoes ?? new GeoCepCacheOptions()),
            NullLogger<CepResolver>.Instance);
}
