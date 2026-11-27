namespace Unifesspa.Geo.API.Formatting;

using System.Text.RegularExpressions;

using Unifesspa.Geo.API.Errors;
using Unifesspa.Geo.Kernel.Results;

/// <summary>Formato da UF: exatamente 2 letras ASCII.</summary>
internal static partial class FormatoDaUf
{
    public static DomainError? Validar(string? uf) =>
        !string.IsNullOrWhiteSpace(uf) && UfRegex().IsMatch(uf)
            ? null
            : ErrosDasConsultas.ErroUfInvalida();

    [GeneratedRegex(@"^[A-Za-z]{2}$")]
    private static partial Regex UfRegex();
}
