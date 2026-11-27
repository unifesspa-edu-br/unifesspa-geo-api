namespace Unifesspa.Geo.Infrastructure.Core.Middleware;

using System.Collections.Frozen;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

public sealed class QueryStringMasker
{
    private readonly FrozenSet<string> _nomesSensiveis;
    private readonly string _valorMascarado;

    public QueryStringMasker(IOptions<RequestLoggingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        RequestLoggingOptions valor = options.Value;

        // FrozenSet: build custoso amortizado (singleton), lookup mais rápido que HashSet.
        _nomesSensiveis = FrozenSet.ToFrozenSet(valor.NomesParametrosSensiveis, StringComparer.OrdinalIgnoreCase);
        _valorMascarado = valor.ValorMascarado;
    }

    public string Mascarar(QueryString queryString)
    {
        if (!queryString.HasValue || queryString.Value!.Length <= 1)
        {
            return queryString.Value ?? string.Empty;
        }

        string[] parametros = queryString.Value![1..].Split('&', StringSplitOptions.RemoveEmptyEntries);
        return "?" + string.Join('&', parametros.Select(MascararParametro));
    }

    private string MascararParametro(string parametro)
    {
        int igual = parametro.IndexOf('=', StringComparison.Ordinal);
        if (igual < 0)
        {
            return parametro;
        }

        string chaveCodificada = parametro[..igual];
        // Segurança: compara a chave decodificada para não deixar bypass via
        // percent-encoding (ex.: `?%63%70%66=123` equivale a `?cpf=123`).
        string chave = Uri.UnescapeDataString(chaveCodificada);
        string valor = _nomesSensiveis.Contains(chave) ? _valorMascarado : parametro[(igual + 1)..];
        return $"{chaveCodificada}={valor}";
    }
}
