namespace Unifesspa.Geo.Infrastructure.Core.Middleware;

using System.Diagnostics;
using System.Text.RegularExpressions;

using Serilog.Context;

/// <summary>
/// Regra única do identificador de correlação, usada na entrada HTTP e na mensageria:
/// quais valores são aceitos, a tag no trace e o escopo de log.
/// </summary>
internal static partial class IdDeCorrelacao
{
    /// <summary>
    /// Devolve o candidato se ele cabe no formato aceito; nulo se estiver ausente ou inválido.
    /// </summary>
    internal static string? SeValido(string? candidato) =>
        !string.IsNullOrEmpty(candidato) && FormatoValido().IsMatch(candidato) ? candidato : null;

    internal static void MarcarNoRastreamento(string id)
    {
        // Propagar o identificador como atributo do span habilita o drill-down
        // Loki → Tempo via derivedFields no Grafana (ADR-0018) e fecha o ciclo
        // log ↔ trace. Activity.Current pode ser null quando não há OTel ligado
        // (ex.: testes sem listener); o ?. mantém o chamador seguro nesse cenário.
        Activity.Current?.SetTag(CorrelationIdMiddleware.ActivityTagName, id);
    }

    internal static IDisposable AbrirEscopoDeLog(string id) =>
        LogContext.PushProperty(CorrelationIdMiddleware.LogContextProperty, id);

    // Restringe valores aceitos do cliente a ASCII imprimível [A-Za-z0-9\-_.],
    // 1..128 chars. Rejeita controle (\r\n, NULL), whitespace e não-ASCII —
    // previne log injection e poluição de dashboards estruturados downstream.
    // O limite superior do quantificador deve espelhar MaxCorrelationIdLength.
    // As duas portas de entrada (HTTP e mensageria) validam com esta mesma regra,
    // para o formato do identificador ser uniforme em qualquer transporte.
    [GeneratedRegex(@"^[A-Za-z0-9\-_.]{1,128}$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FormatoValido();
}
