namespace Unifesspa.Geo.Infrastructure.Core.Logging;

using System.Collections.Generic;
using System.Text.RegularExpressions;

using Serilog.Core;
using Serilog.Events;

public sealed partial class PiiMaskingEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        foreach (KeyValuePair<string, LogEventPropertyValue> propriedade in logEvent.Properties.ToArray())
        {
            LogEventPropertyValue mascarado = MascararValor(propriedade.Value);
            if (!ReferenceEquals(mascarado, propriedade.Value))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(propriedade.Key, mascarado));
            }
        }
    }

    internal static string MascararCpf(string texto)
    {
        if (string.IsNullOrEmpty(texto))
        {
            return texto;
        }

        return CpfPattern().Replace(texto, MascararCpfEncontrado);
    }

    private static string MascararCpfEncontrado(Match encontrado)
    {
        Span<char> digitos = stackalloc char[11];
        int count = 0;
        foreach (char c in encontrado.ValueSpan)
        {
            if (char.IsAsciiDigit(c))
                digitos[count++] = c;
        }
        return count == 11
            ? $"***.{new string(digitos[3..6])}.{new string(digitos[6..9])}-**"
            : "***.***.***-**";
    }

    private static LogEventPropertyValue MascararValor(LogEventPropertyValue valor) => valor switch
    {
        ScalarValue scalar when scalar.Value is string texto => MascararScalar(scalar, texto),
        StructureValue estrutura => MascararEstrutura(estrutura),
        SequenceValue sequencia => MascararSequencia(sequencia),
        DictionaryValue dicionario => MascararDicionario(dicionario),
        _ => valor,
    };

    private static ScalarValue MascararScalar(ScalarValue original, string texto)
    {
        string mascarado = MascararCpf(texto);
        return ReferenceEquals(mascarado, texto) ? original : new ScalarValue(mascarado);
    }

    private static StructureValue MascararEstrutura(StructureValue original)
    {
        LogEventProperty[]? mascaradas = CopiaSoSeAlgoMudou(original.Properties, original.Properties.Count, MascararPropriedade);
        return mascaradas is null ? original : new StructureValue(mascaradas, original.TypeTag);
    }

    private static SequenceValue MascararSequencia(SequenceValue original)
    {
        LogEventPropertyValue[]? mascarados = CopiaSoSeAlgoMudou(original.Elements, original.Elements.Count, MascararElemento);
        return mascarados is null ? original : new SequenceValue(mascarados);
    }

    private static DictionaryValue MascararDicionario(DictionaryValue original)
    {
        KeyValuePair<ScalarValue, LogEventPropertyValue>[]? mascaradas =
            CopiaSoSeAlgoMudou(original.Elements, original.Elements.Count, MascararEntrada);
        return mascaradas is null ? original : new DictionaryValue(mascaradas);
    }

    // A estrutura só é recriada se algum valor foi mascarado: antes da primeira mudança copia os
    // originais; depois dela, os inalterados ficam como estão. A igualdade por referência indica "não mudou".
    private static T[]? CopiaSoSeAlgoMudou<T>(IEnumerable<T> originais, int quantidade, Func<T, (T Valor, bool Mudou)> mascarar)
    {
        T[]? copia = null;
        int indice = 0;
        foreach (T original in originais)
        {
            (T valor, bool mudou) = mascarar(original);
            copia ??= mudou ? CopiarOsAnteriores(originais, quantidade, indice) : null;
            copia?[indice] = valor;
            indice++;
        }

        return copia;
    }

    private static T[] CopiarOsAnteriores<T>(IEnumerable<T> originais, int quantidade, int ate)
    {
        T[] copia = new T[quantidade];
        int indice = 0;
        foreach (T anterior in originais.Take(ate))
        {
            copia[indice++] = anterior;
        }

        return copia;
    }

    private static (LogEventProperty, bool) MascararPropriedade(LogEventProperty propriedade)
    {
        LogEventPropertyValue valor = MascararValor(propriedade.Value);
        return ReferenceEquals(valor, propriedade.Value)
            ? (propriedade, false)
            : (new LogEventProperty(propriedade.Name, valor), true);
    }

    private static (LogEventPropertyValue, bool) MascararElemento(LogEventPropertyValue elemento)
    {
        LogEventPropertyValue valor = MascararValor(elemento);
        return (valor, !ReferenceEquals(valor, elemento));
    }

    private static (KeyValuePair<ScalarValue, LogEventPropertyValue>, bool) MascararEntrada(
        KeyValuePair<ScalarValue, LogEventPropertyValue> entrada)
    {
        ScalarValue chave = entrada.Key.Value is string textoChave ? MascararScalar(entrada.Key, textoChave) : entrada.Key;
        LogEventPropertyValue valor = MascararValor(entrada.Value);
        bool mudou = !ReferenceEquals(chave, entrada.Key) || !ReferenceEquals(valor, entrada.Value);
        return (mudou ? new KeyValuePair<ScalarValue, LogEventPropertyValue>(chave, valor) : entrada, mudou);
    }

    // Word boundaries ((?<!\d) / (?!\d)) impedem casar sub-sequências de 11 dígitos
    // dentro de números maiores (timestamps, IDs), evitando tanto falsos positivos
    // (log corruption) quanto falsos negativos onde dígitos do CPF vazariam fora do match.
    [GeneratedRegex(@"(?<![0-9])[0-9]{3}\.?[0-9]{3}\.?[0-9]{3}-?[0-9]{2}(?![0-9])", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CpfPattern();
}
