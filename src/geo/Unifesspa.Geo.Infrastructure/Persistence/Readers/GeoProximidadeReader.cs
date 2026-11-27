namespace Unifesspa.Geo.Infrastructure.Persistence.Readers;

using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore;

using NetTopologySuite.Geometries;

using Unifesspa.Geo.Application.Abstractions;
using Unifesspa.Geo.Application.DTOs;
using Unifesspa.Geo.Domain.Entities;

/// <summary>
/// Leitor geoespacial de proximidade (ADR-0091) sobre o <see cref="GeoDbContext"/>.
/// Só expõe reference data vigente (ADR-0092). O filtro de raio usa
/// <c>IsWithinDistance</c> → <c>ST_DWithin</c> (<em>index-friendly</em>, usa o índice
/// GIST da coordenada), e a ordenação usa <c>Distance</c> → <c>ST_Distance</c>
/// (metros sobre o esferoide, pois a coluna é <c>geography</c>). O ponto é sempre
/// construído como <c>Point(longitude, latitude)</c> (X=long, Y=lat) — invertê-los
/// é silenciosamente errado.
/// </summary>
/// <remarks>
/// Registros sem coordenada (<c>Coordenada</c>/<c>Latitude</c>/<c>Longitude</c> nulos)
/// são excluídos: o DTO expõe <c>Latitude</c>/<c>Longitude</c> como <c>decimal</c> não
/// nulo, então o guard explícito evita materializar linha incompleta. O desempate
/// estável por <c>Id</c> torna o top-N determinístico quando distâncias empatam.
/// </remarks>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instanciada via DI em GeoInfrastructureRegistration.")]
internal sealed class GeoProximidadeReader : IGeoProximidadeReader
{
    private const double MetrosPorQuilometro = 1000.0;

    private readonly GeoDbContext _dbContext;

    public GeoProximidadeReader(GeoDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<CidadeProximaDto>> BuscarCidadesProximasAsync(
        double latitude,
        double longitude,
        double raioKm,
        int limit,
        CancellationToken cancellationToken)
    {
        Point ponto = CriarPonto(longitude, latitude);

        return await CidadesVigentesNoRaio(ponto, raioKm * MetrosPorQuilometro)
            .Take(limit)
            .Select(ComoCidadeProxima(ponto))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LogradouroProximoDto>> BuscarLogradourosProximosAsync(
        double latitude,
        double longitude,
        double raioKm,
        int limit,
        CancellationToken cancellationToken)
    {
        Point ponto = CriarPonto(longitude, latitude);

        return await ConsultaDosLogradourosProximos(ponto, raioKm * MetrosPorQuilometro)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private IOrderedQueryable<Cidade> CidadesVigentesNoRaio(Point ponto, double raioMetros) =>
        _dbContext.Cidades
            .AsNoTracking()
            .Where(c => c.Vigente
                     && c.Coordenada != null
                     && c.Latitude != null
                     && c.Longitude != null
                     && c.Coordenada.IsWithinDistance(ponto, raioMetros))
            .OrderBy(c => c.Coordenada!.Distance(ponto))
            .ThenBy(c => c.Id);

    private static Expression<Func<Cidade, CidadeProximaDto>> ComoCidadeProxima(Point ponto) =>
        c => new CidadeProximaDto(
            c.Id,
            c.CodigoIbge,
            c.Nome,
            c.Uf,
            c.Latitude!.Value,
            c.Longitude!.Value,
            c.Coordenada!.Distance(ponto) / MetrosPorQuilometro);

    // Join com a cidade vigente para enriquecer com nome + código IBGE (padrão
    // CepReader): o Logradouro guarda só CidadeId/Uf, não o nome/código da cidade.
    private IQueryable<LogradouroProximoDto> ConsultaDosLogradourosProximos(Point ponto, double raioMetros) =>
        from l in LogradourosVigentesNoRaio(ponto, raioMetros)
        join c in _dbContext.Cidades.AsNoTracking().Where(c => c.Vigente) on l.CidadeId equals c.Id
        orderby l.Coordenada!.Distance(ponto), l.Id
        select new LogradouroProximoDto(
            l.Id,
            l.Cep,
            l.Tipo,
            l.Nome,
            c.Nome,
            c.CodigoIbge,
            l.Uf,
            l.Latitude!.Value,
            l.Longitude!.Value,
            l.Coordenada!.Distance(ponto) / MetrosPorQuilometro);

    private IQueryable<Logradouro> LogradourosVigentesNoRaio(Point ponto, double raioMetros) =>
        _dbContext.Logradouros
            .AsNoTracking()
            .Where(l => l.Vigente
                     && l.Coordenada != null
                     && l.Latitude != null
                     && l.Longitude != null
                     && l.Coordenada.IsWithinDistance(ponto, raioMetros));

    // Convenção NetTopologySuite/PostGIS: X = longitude, Y = latitude. SRID 4326 (WGS84).
    private static Point CriarPonto(double longitude, double latitude) =>
        new(longitude, latitude) { SRID = 4326 };
}
