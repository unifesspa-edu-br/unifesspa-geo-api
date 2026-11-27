namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

using System.Buffers;

/// <summary>Montagem e leitura do nome <c>tbl_cep_{versao}_n_{tabela}</c> usado nas entradas e nos comandos.</summary>
internal static class NomeDaTabelaDne
{
    private const string Prefixo = "tbl_cep_";
    private const string Meio = "_n_";
    private const string ExtensaoDaEntrada = ".sql";
    private const int DigitosDaVersao = 6;

    private static readonly SearchValues<char> CaracteresDoNomeDaTabela =
        SearchValues.Create("abcdefghijklmnopqrstuvwxyz_");

    public static string Base(string versao, string tabela) => $"{Prefixo}{versao}{Meio}{tabela}";

    public static string Entrada(string versao, string tabela) => Base(versao, tabela) + ExtensaoDaEntrada;

    /// <summary>Lê o nome de uma entrada <c>.sql</c>; não confere se a tabela é uma das 15.</summary>
    public static bool TentarLerEntrada(string nome, out string versao, out string tabela)
    {
        versao = tabela = string.Empty;
        return nome.EndsWith(ExtensaoDaEntrada, StringComparison.Ordinal)
            && TentarLerBase(nome[..^ExtensaoDaEntrada.Length], out versao, out tabela);
    }

    private static bool TentarLerBase(string texto, out string versao, out string tabela)
    {
        bool valido = EstruturaEhValida(texto);
        versao = valido ? texto.Substring(Prefixo.Length, DigitosDaVersao) : string.Empty;
        tabela = valido ? texto[(Prefixo.Length + DigitosDaVersao + Meio.Length)..] : string.Empty;
        return valido;
    }

    private static bool EstruturaEhValida(string texto)
    {
        int inicioDoMeio = Prefixo.Length + DigitosDaVersao;
        if (!texto.StartsWith(Prefixo, StringComparison.Ordinal) || texto.Length <= inicioDoMeio + Meio.Length)
        {
            return false;
        }

        ReadOnlySpan<char> resto = texto.AsSpan(inicioDoMeio);
        return texto.AsSpan(Prefixo.Length, DigitosDaVersao).IndexOfAnyExceptInRange('0', '9') < 0
            && resto.StartsWith(Meio, StringComparison.Ordinal)
            && resto[Meio.Length..].IndexOfAnyExcept(CaracteresDoNomeDaTabela) < 0;
    }
}
