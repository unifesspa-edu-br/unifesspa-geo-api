namespace Unifesspa.Geo.Domain.ValueObjects;

using NetTopologySuite.Geometries;

/// <summary>
/// Posição de uma localidade ou de um endereço: latitude e longitude em graus e o ponto
/// georreferenciado (SRID 4326) que a carga monta a partir delas. Qualquer parte pode faltar.
/// </summary>
public readonly record struct PosicaoGeografica(decimal? Latitude, decimal? Longitude, Point? Ponto)
{
    private const int Srid4326 = 4326;
    private const decimal LatitudeMaxima = 90m;
    private const decimal LongitudeMaxima = 180m;

    /// <summary>Posição sem latitude, sem longitude e sem ponto.</summary>
    public static PosicaoGeografica Ausente => default;

    /// <summary>
    /// Cria a posição só quando latitude (-90 a 90) e longitude (-180 a 180) estão presentes e dentro
    /// do domínio geográfico; caso contrário devolve <see cref="Ausente"/>, de modo que um valor
    /// inválido nunca deixa o outro gravado sozinho. Latitude e longitude ficam como foram lidas (a coluna
    /// numérica arredonda). O ponto é <c>geography(Point,4326)</c>
    /// (ADR-0091), com X = longitude e Y = latitude.
    /// </summary>
    public static PosicaoGeografica Validada(decimal? latitude, decimal? longitude)
    {
        if (latitude is null || longitude is null
            || Math.Abs(latitude.Value) > LatitudeMaxima
            || Math.Abs(longitude.Value) > LongitudeMaxima)
        {
            return Ausente;
        }

        Point ponto = new((double)longitude.Value, (double)latitude.Value) { SRID = Srid4326 };
        return new PosicaoGeografica(latitude, longitude, ponto);
    }
}
