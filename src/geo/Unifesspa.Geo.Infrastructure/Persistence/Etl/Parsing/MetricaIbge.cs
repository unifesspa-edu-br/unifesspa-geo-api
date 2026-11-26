namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Parsing;

/// <summary>
/// Leitura tolerante das métricas do IBGE que a DNE traz como texto: valor ausente
/// ('-', vazio ou inválido) vira <see langword="null"/> e a degradação é registrada no
/// relatório para auditoria. Sem dado pessoal — são métricas socioeconômicas públicas.
/// </summary>
internal static class MetricaIbge
{
    public static decimal? Decimal(string? cru, string coluna, ContadorTabela contador)
    {
        decimal? valor = ParseTolerante.ParaDecimal(cru);
        RegistrarDegradacao(valor is null, cru, coluna, contador);
        return valor;
    }

    public static int? Inteira(string? cru, string coluna, ContadorTabela contador)
    {
        int? valor = ParseTolerante.ParaInteiro(cru);
        RegistrarDegradacao(valor is null, cru, coluna, contador);
        return valor;
    }

    private static void RegistrarDegradacao(bool semValor, string? cru, string coluna, ContadorTabela contador)
    {
        if (semValor && !string.IsNullOrWhiteSpace(cru))
        {
            contador.ContarParseDegradado($"{coluna}={cru.Trim()}");
        }
    }
}
