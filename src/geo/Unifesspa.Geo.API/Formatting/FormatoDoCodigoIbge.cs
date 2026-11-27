namespace Unifesspa.Geo.API.Formatting;

using System.Text.RegularExpressions;

using Unifesspa.Geo.API.Errors;
using Unifesspa.Geo.Kernel.Results;

/// <summary>Formato do código IBGE da cidade: exatamente 7 dígitos ASCII.</summary>
internal static partial class FormatoDoCodigoIbge
{
    public static DomainError? Validar(string? codigoIbge) =>
        !string.IsNullOrWhiteSpace(codigoIbge) && CodigoIbgeRegex().IsMatch(codigoIbge)
            ? null
            : ErrosDasConsultas.ErroCodigoIbgeInvalido();

    // [0-9] e não \d: em .NET \d casa dígitos Unicode (ex.: árabe-índicos); a chave
    // IBGE é ASCII — um "7 dígitos" não-ASCII deve ser 400 (formato), não 404.
    [GeneratedRegex(@"^[0-9]{7}$")]
    private static partial Regex CodigoIbgeRegex();
}
