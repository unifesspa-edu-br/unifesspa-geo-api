namespace Unifesspa.Geo.Infrastructure.Persistence.Readers;

using Microsoft.EntityFrameworkCore;

using Unifesspa.Geo.Domain.Entities;

/// <summary>
/// Cidade vigente (ADR-0092) localizada pelo código IBGE, regra que os leitores de distrito,
/// bairro, logradouro e detalhe de cidade repetiam.
/// </summary>
internal static class ConsultasDaCidadeVigente
{
    /// <summary>Cidade vigente de código IBGE igual ao informado (aparado), sem rastreamento.</summary>
    public static IQueryable<Cidade> CidadeVigentePeloCodigoIbge(this GeoDbContext contexto, string codigoIbge)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(codigoIbge);

        string codigo = codigoIbge.Trim();
        return contexto.Cidades
            .AsNoTracking()
            .Where(c => c.Vigente && c.CodigoIbge == codigo);
    }

    /// <summary>Identificador da cidade vigente de código IBGE igual ao informado, ou nulo se não houver.</summary>
    public static Task<Guid?> IdDaCidadeVigenteAsync(this GeoDbContext contexto, string codigoIbge, CancellationToken cancellationToken) =>
        contexto
            .CidadeVigentePeloCodigoIbge(codigoIbge)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);
}
