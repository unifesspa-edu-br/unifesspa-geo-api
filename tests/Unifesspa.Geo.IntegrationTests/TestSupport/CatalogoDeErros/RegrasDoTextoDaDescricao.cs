namespace Unifesspa.Geo.IntegrationTests.TestSupport.CatalogoDeErros;

using System.Text.RegularExpressions;

/// <summary>
/// Regras do texto de uma descrição que o portal não confere: formato do requisito, forma do código
/// e ausência de dado pessoal, inclusive dentro de blocos de código.
/// </summary>
internal static partial class RegrasDoTextoDaDescricao
{
    internal static IEnumerable<string> Conferir(string texto)
    {
        if (CpfFormatado().IsMatch(texto) || CpfSemPontuacao().IsMatch(texto))
        {
            yield return "CPF";
        }

        if (DigitosConsecutivos().IsMatch(texto))
        {
            yield return "sequência de 11 ou mais dígitos";
        }

        if (Email().IsMatch(texto))
        {
            yield return "e-mail";
        }
    }

    [GeneratedRegex(@"\AUNI-REQ-[0-9]{4}\z", RegexOptions.CultureInvariant)]
    internal static partial Regex Requisito();

    [GeneratedRegex(@"\Auniplus(?:\.[a-z0-9_]+)+\z", RegexOptions.CultureInvariant)]
    internal static partial Regex CodigoPublico();

    [GeneratedRegex(@"(?<!\d)\d{3}\.\d{3}\.\d{3}-\d{2}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex CpfFormatado();

    [GeneratedRegex(@"(?<!\d)\d{11}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex CpfSemPontuacao();

    [GeneratedRegex(@"\d{11,}", RegexOptions.CultureInvariant)]
    private static partial Regex DigitosConsecutivos();

    [GeneratedRegex(@"[\w.!#$%&'*+/=?^\x60{|}~-]+@(?:[\w-]+(?:\.[\w-]+)+|\[[^\]\s]+\])", RegexOptions.CultureInvariant)]
    private static partial Regex Email();
}
