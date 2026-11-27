namespace Unifesspa.Geo.API.Formatting;

using Unifesspa.Geo.API.Errors;
using Unifesspa.Geo.Kernel.Results;

/// <summary>
/// Validação no boundary HTTP (ADR-0031) dos parâmetros da consulta de proximidade:
/// <c>lat</c>/<c>long</c>/<c>raioKm</c> obrigatórios e em faixa, <c>limit</c> com
/// default e teto. O handler recebe a consulta já validada (nunca a entrada crua);
/// qualquer parâmetro ausente, não finito ou fora de faixa vira recusa 400 com código próprio.
/// </summary>
internal sealed class ConsultaProximidade
{
    private ConsultaProximidade(double latitude, double longitude, double raioKm, int limit)
    {
        Latitude = latitude;
        Longitude = longitude;
        RaioKm = raioKm;
        Limit = limit;
    }

    public double Latitude { get; }

    public double Longitude { get; }

    public double RaioKm { get; }

    public int Limit { get; }

    /// <summary>
    /// Valida os parâmetros contra <paramref name="opcoes"/> (teto de raio, default/teto
    /// de limit). A primeira recusa, na ordem parâmetros, latitude, longitude, raio e limite,
    /// é a devolvida; <c>limit</c> acima do teto é truncado, não recusado.
    /// </summary>
    public static Result<ConsultaProximidade> Criar(double? lat, double? @long, double? raioKm, int? limit, GeoProximidadeOptions opcoes)
    {
        ArgumentNullException.ThrowIfNull(opcoes);

        if (lat is not { } latitude || @long is not { } longitude || raioKm is not { } raio)
        {
            return Result<ConsultaProximidade>.Failure(ErrosDasConsultas.ErroParametrosDeProximidadeAusentes());
        }

        DomainError? recusa = RecusaDaLatitude(latitude) ?? RecusaDaLongitude(longitude)
            ?? RecusaDoRaio(raio, opcoes) ?? RecusaDoLimite(limit);

        return recusa is not null
            ? Result<ConsultaProximidade>.Failure(recusa)
            : Result<ConsultaProximidade>.Success(new ConsultaProximidade(latitude, longitude, raio, LimiteEfetivo(limit, opcoes)));
    }

    private static DomainError? RecusaDaLatitude(double latitude) =>
        double.IsFinite(latitude) && latitude is >= -90 and <= 90 ? null : ErrosDasConsultas.ErroLatitudeInvalida();

    private static DomainError? RecusaDaLongitude(double longitude) =>
        double.IsFinite(longitude) && longitude is >= -180 and <= 180 ? null : ErrosDasConsultas.ErroLongitudeInvalida();

    private static DomainError? RecusaDoRaio(double raio, GeoProximidadeOptions opcoes) =>
        double.IsFinite(raio) && raio > 0 && raio <= opcoes.RaioMaxKm ? null : ErrosDasConsultas.ErroRaioInvalido(opcoes.RaioMaxKm);

    // limit negativo ou zero é entrada inválida (não se silencia com clamp); ausente usa o default.
    private static DomainError? RecusaDoLimite(int? limit) =>
        limit is <= 0 ? ErrosDasConsultas.ErroLimiteInvalido() : null;

    // Acima do teto, trunca para o teto (top-N defensivo).
    private static int LimiteEfetivo(int? limit, GeoProximidadeOptions opcoes) =>
        Math.Min(limit ?? opcoes.LimitPadrao, opcoes.LimitMax);
}
