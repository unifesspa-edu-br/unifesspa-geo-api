namespace Unifesspa.Geo.IntegrationTests.Etl;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using Unifesspa.Geo.Application.Abstractions;
using Unifesspa.Geo.Application.DTOs;
using Unifesspa.Geo.Infrastructure.Caching;
using Unifesspa.Geo.Infrastructure.Cep;
using Unifesspa.Geo.Infrastructure.Core.Caching;

using static Unifesspa.Geo.IntegrationTests.Etl.CenarioDaCargaPeriodica;

/// <summary>
/// Cache do CEP de Marabá para provar o selo da carga: o resolvedor real guarda a resposta no
/// cache em memória e o leitor substituto conta quantas vezes o banco foi consultado. O
/// <see cref="Invalidador"/> é o que a carga aciona ao selar a versão vigente.
/// </summary>
internal sealed class CacheDoCepParaTeste : IDisposable
{
    private readonly MemoryCache _memoria = new(new MemoryCacheOptions());

    public CacheDoCepParaTeste(TimeProvider relogio)
    {
        MemoriaCacheFake cache = new();
        Invalidador = new RedisGeoCepCacheInvalidador(cache, NullLogger<RedisGeoCepCacheInvalidador>.Instance);
        Leitor.ResolverAsync(CepMaraba, Arg.Any<CancellationToken>()).Returns(
            new CepResolvidoDto(CepMaraba, null, null, null, null, null, "Marabá", Maraba, "PA", null, null, CepResolucao.NivelCidade, CepResolucao.OrigemFaixaCidade));
        Resolver = new CepResolver(
            new Lazy<ICacheService>(() => cache), Leitor, _memoria, relogio,
            Microsoft.Extensions.Options.Options.Create(new GeoCepCacheOptions { SeloTtl = TimeSpan.Zero }),
            NullLogger<CepResolver>.Instance);
    }

    public IGeoCepCacheInvalidador Invalidador { get; }

    public ICepReader Leitor { get; } = Substitute.For<ICepReader>();

    public CepResolver Resolver { get; }

    public void Dispose() => _memoria.Dispose();
}
