namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

using System.Globalization;
using System.Text;

/// <summary>
/// Lê a tupla de um <c>INSERT ... VALUES (...)</c> sem executar nada. Só aceita, separados por
/// vírgula: <c>NULL</c>, inteiro de 32 bits com sinal opcional e texto entre aspas simples com
/// aspa dobrada como escape. Qualquer outra forma (<c>E'..'</c>, <c>$$</c>, <c>::</c>, funções,
/// outro comando depois do texto) recusa a linha inteira.
/// </summary>
internal static class ValoresDoInsert
{
    private const string LiteralNulo = "NULL";

    /// <summary>
    /// Lê os valores da tupla (o texto entre o parêntese de abertura e o de fechamento).
    /// Valores: <see langword="null"/>, <see cref="int"/> ou <see cref="string"/>.
    /// </summary>
    public static bool TentarLer(ReadOnlySpan<char> tupla, out object?[] valores)
    {
        List<object?> lidos = [];
        int posicao = 0;
        bool fim;
        do
        {
            if (!TentarLerItem(tupla, ref posicao, lidos, out fim))
            {
                valores = [];
                return false;
            }
        }
        while (!fim);

        valores = [.. lidos];
        return true;
    }

    private static bool TentarLerItem(ReadOnlySpan<char> tupla, ref int posicao, List<object?> lidos, out bool fim)
    {
        fim = false;
        posicao = PularEspacos(tupla, posicao);
        if (!TentarLerValor(tupla, ref posicao, out object? valor))
        {
            return false;
        }

        lidos.Add(valor);
        posicao = PularEspacos(tupla, posicao);
        fim = posicao == tupla.Length;
        return fim || tupla[posicao++] == ',';
    }

    private static int PularEspacos(ReadOnlySpan<char> tupla, int posicao)
    {
        while (posicao < tupla.Length && tupla[posicao] is ' ' or '\t')
        {
            posicao++;
        }

        return posicao;
    }

    private static bool TentarLerValor(ReadOnlySpan<char> tupla, ref int posicao, out object? valor)
    {
        valor = null;
        if (posicao >= tupla.Length)
        {
            return false;
        }

        char inicial = tupla[posicao];
        return inicial switch
        {
            '\'' => TentarLerTexto(tupla, ref posicao, out valor),
            'N' => TentarLerNulo(tupla, ref posicao),
            '+' or '-' or (>= '0' and <= '9') => TentarLerInteiro(tupla, ref posicao, out valor),
            _ => false,
        };
    }

    private static bool TentarLerNulo(ReadOnlySpan<char> tupla, ref int posicao)
    {
        if (!tupla[posicao..].StartsWith(LiteralNulo, StringComparison.Ordinal))
        {
            return false;
        }

        posicao += LiteralNulo.Length;
        return true;
    }

    private static bool TentarLerInteiro(ReadOnlySpan<char> tupla, ref int posicao, out object? valor)
    {
        int inicio = posicao;
        if (tupla[posicao] is '+' or '-')
        {
            posicao++;
        }

        while (posicao < tupla.Length && tupla[posicao] is >= '0' and <= '9')
        {
            posicao++;
        }

        bool valido = int.TryParse(
            tupla[inicio..posicao], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int inteiro);
        valor = valido ? inteiro : null;
        return valido;
    }

    private static bool TentarLerTexto(ReadOnlySpan<char> tupla, ref int posicao, out object? valor)
    {
        StringBuilder texto = new();
        valor = null;
        posicao++;
        while (posicao < tupla.Length)
        {
            PassoDoTexto passo = LerCaractere(tupla, ref posicao, texto);
            if (passo != PassoDoTexto.Continua)
            {
                valor = passo == PassoDoTexto.Fechou ? texto.ToString() : null;
                return passo == PassoDoTexto.Fechou;
            }
        }

        return false;
    }

    private static PassoDoTexto LerCaractere(ReadOnlySpan<char> tupla, ref int posicao, StringBuilder texto)
    {
        char atual = tupla[posicao++];
        if (atual == '\0')
        {
            return PassoDoTexto.Invalido;
        }

        if (atual == '\'' && !(posicao < tupla.Length && tupla[posicao] == '\''))
        {
            return PassoDoTexto.Fechou;
        }

        texto.Append(atual);
        posicao += atual == '\'' ? 1 : 0;
        return PassoDoTexto.Continua;
    }

    private enum PassoDoTexto
    {
        Continua,
        Fechou,
        Invalido,
    }
}
