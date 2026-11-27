namespace Unifesspa.Geo.Infrastructure.Core.Pagination;

using System.Globalization;

using Microsoft.AspNetCore.Mvc;

using Unifesspa.Geo.Kernel.Pagination;

/// <summary>
/// Extensões de <see cref="ControllerBase"/> para responder coleções
/// paginadas com cursor (ADR-0026): encoda o cursor da próxima página,
/// monta o header <c>Link</c> (RFC 5988/8288) e o header <c>X-Page-Size</c>,
/// e devolve o body como array JSON puro (ADR-0025). O recurso e o vínculo
/// ao usuário do cursor vêm do <see cref="FromCursorAttribute"/> da ação.
/// </summary>
public static class PaginationControllerExtensions
{
    /// <summary>
    /// Devolve <see cref="StatusCodes.Status200OK"/> com os <paramref name="items"/>
    /// como array JSON; encoda o cursor da próxima página quando
    /// <paramref name="nextAfterId"/> não é <c>null</c>; popula
    /// <c>Link</c> e <c>X-Page-Size</c>. Variante keyset por <c>Id</c> (a maioria dos
    /// recursos) — a âncora do cursor é só o <c>Id</c>.
    /// </summary>
    public static Task<IActionResult> OkPaginatedAsync<T>(
        this ControllerBase controller,
        IReadOnlyList<T> items,
        Guid? prevAfterId,
        Guid? nextAfterId,
        PageRequest page,
        CancellationToken cancellationToken = default) =>
        ResponderPaginaAsync(controller, items, AncorasDaPagina.PorId(prevAfterId, nextAfterId), page, cancellationToken);

    /// <summary>
    /// Variante para keyset multi-coluna ordenado (ADR-0094): cada âncora carrega a
    /// chave de ordenação <strong>e</strong> o <c>Id</c> de desempate, serializados no
    /// payload opaco. A camada de cursor (AES-GCM, TTL, user-binding, Link) é a mesma.
    /// </summary>
    public static Task<IActionResult> OkPaginatedOrdenadoAsync<T>(
        this ControllerBase controller,
        IReadOnlyList<T> items,
        (string SortKey, Guid Id)? prev,
        (string SortKey, Guid Id)? next,
        PageRequest page,
        CancellationToken cancellationToken = default) =>
        ResponderPaginaAsync(controller, items, AncorasDaPagina.Ordenadas(prev, next), page, cancellationToken);

    private static async Task<IActionResult> ResponderPaginaAsync<T>(
        ControllerBase controller,
        IReadOnlyList<T> items,
        AncorasDaPagina ancoras,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(page);

        EmissaoDosCursores emissao = EmissaoDosCursores.Para(controller, ancoras);
        string? anterior = await emissao.EmitirAsync(ancoras.Anterior, PaginationDirection.Prev, page.Limit, cancellationToken).ConfigureAwait(false);
        string? seguinte = await emissao.EmitirAsync(ancoras.Proxima, PaginationDirection.Next, page.Limit, cancellationToken).ConfigureAwait(false);

        controller.Response.Headers["Link"] = LinkHeaderBuilder.Build(EnderecosDaNavegacao.Da(controller.Request, anterior, seguinte));
        controller.Response.Headers["X-Page-Size"] = items.Count.ToString(CultureInfo.InvariantCulture);

        return controller.Ok(items);
    }
}
