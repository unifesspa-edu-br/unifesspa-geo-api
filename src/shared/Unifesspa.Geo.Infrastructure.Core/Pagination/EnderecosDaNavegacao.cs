namespace Unifesspa.Geo.Infrastructure.Core.Pagination;

using System.Globalization;

using Microsoft.AspNetCore.Http;

/// <summary>
/// Monta os endereços de navegação (RFC 5988/8288) de uma página: o próprio endereço pedido e os
/// das páginas anterior e seguinte.
/// </summary>
internal static class EnderecosDaNavegacao
{
    private static readonly string[] ParametrosDaPaginacaoReservados = ["cursor", "limit", "direction"];

    // O próprio endereço espelha o que veio na requisição (cursor, limit e direction como vieram).
    // Anterior e seguinte não levam limit, porque o cursor já carrega a janela (ajustada na
    // leitura), e levam a direção explícita: o link fica auto-suficiente e o cliente o segue sem
    // conhecer a convenção.
    public static PageLinks Da(HttpRequest request, string? cursorAnterior, string? cursorSeguinte)
    {
        IQueryCollection consulta = request.Query;

        return new PageLinks(
            Self: Endereco(request, consulta["cursor"], LimiteDaRequisicao(consulta), consulta["direction"]),
            Next: cursorSeguinte is null ? null : Endereco(request, cursorSeguinte, limite: null, "next"),
            Prev: cursorAnterior is null ? null : Endereco(request, cursorAnterior, limite: null, "prev"));
    }

    // Inclui o PathBase para honrar a implantação atrás de proxy reverso ou com
    // app.UsePathBase("/foo"): sem ele o link apontaria para /api/editais em vez de
    // /foo/api/editais e o cliente receberia 404 ao segui-lo.
    public static string Endereco(HttpRequest request, string? cursor, int? limite, string? direcao)
    {
        string enderecoBase = $"{request.Scheme}://{request.Host}{request.PathBase}{request.Path}";
        string[] parametros = [.. ParametrosDaPaginacao(cursor, limite, direcao), .. FiltrosPreservados(request.Query)];

        return parametros.Length == 0 ? enderecoBase : $"{enderecoBase}?{string.Join('&', parametros)}";
    }

    public static IEnumerable<string> ParametrosDaPaginacao(string? cursor, int? limite, string? direcao)
    {
        if (!string.IsNullOrEmpty(cursor))
            yield return $"cursor={Uri.EscapeDataString(cursor)}";
        if (limite is { } valor)
            yield return $"limit={valor.ToString(CultureInfo.InvariantCulture)}";
        if (!string.IsNullOrEmpty(direcao))
            yield return $"direction={Uri.EscapeDataString(direcao)}";
    }

    // Os demais parâmetros da requisição (filtros como q ou tipo) seguem nos links: sem eles,
    // seguir rel="next" de uma listagem filtrada levaria a uma página sem filtro. Os de paginação
    // já foram tratados e os multivalores (tipo=3&tipo=4) são preservados; OfType descarta os
    // elementos nulos de StringValues.
    public static IEnumerable<string> FiltrosPreservados(IQueryCollection consulta) =>
        consulta
            .Where(static parametro => !ParametrosDaPaginacaoReservados.Contains(parametro.Key, StringComparer.OrdinalIgnoreCase))
            .SelectMany(static parametro => parametro.Value
                .OfType<string>()
                .Select(valor => $"{Uri.EscapeDataString(parametro.Key)}={Uri.EscapeDataString(valor)}"));

    public static int? LimiteDaRequisicao(IQueryCollection consulta)
    {
        string? bruto = consulta["limit"];
        return int.TryParse(bruto, NumberStyles.Integer, CultureInfo.InvariantCulture, out int lido) ? lido : null;
    }
}
