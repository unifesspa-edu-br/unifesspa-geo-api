namespace Unifesspa.Geo.IntegrationTests.TestSupport;

/// <summary>
/// CPF fictício gerado a partir de uma semente de até nove dígitos, com os dois dígitos verificadores calculados.
/// Nunca representa pessoa real.
/// </summary>
internal sealed class CpfFicticio
{
    private CpfFicticio(string digitos)
    {
        Digitos = digitos;
    }

    /// <summary>Os onze dígitos, sem pontuação.</summary>
    public string Digitos { get; }

    /// <summary>Formato <c>ddd.ddd.ddd-dd</c>.</summary>
    public string Pontuado =>
        $"{Digitos[..3]}.{Digitos[3..6]}.{Digitos[6..9]}-{Digitos[9..]}";

    /// <summary>Formato mascarado nos registros: <c>***.ddd.ddd-**</c>, com o quarto ao nono dígito.</summary>
    public string Mascarado => $"***.{Digitos[3..6]}.{Digitos[6..9]}-**";

    public static CpfFicticio Gerar(int semente)
    {
        string corpo = semente.ToString("D9", System.Globalization.CultureInfo.InvariantCulture);
        string comPrimeiro = corpo + Verificador(corpo);
        return new CpfFicticio(comPrimeiro + Verificador(comPrimeiro));
    }

    private static int Verificador(string digitos)
    {
        int peso = digitos.Length + 1;
        int soma = digitos.Sum(digito => (digito - '0') * peso--);
        int resto = soma % 11;
        return resto < 2 ? 0 : 11 - resto;
    }
}
