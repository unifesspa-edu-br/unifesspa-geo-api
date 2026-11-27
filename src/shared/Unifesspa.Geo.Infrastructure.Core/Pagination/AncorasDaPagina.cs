namespace Unifesspa.Geo.Infrastructure.Core.Pagination;

/// <summary>
/// As âncoras das páginas vizinhas a emitir como cursor. Âncora ausente = não há página daquele lado.
/// </summary>
internal readonly record struct AncorasDaPagina(AncoraDoCursor? Anterior, AncoraDoCursor? Proxima)
{
    /// <summary>Há ao menos um cursor a emitir.</summary>
    public bool TemAlguma => Anterior is not null || Proxima is not null;

    /// <summary>Keyset por <c>Id</c>: a âncora é só o <c>Id</c> do item de fronteira.</summary>
    public static AncorasDaPagina PorId(Guid? anterior, Guid? proxima) =>
        new(DoId(anterior), DoId(proxima));

    /// <summary>Keyset ordenado: a âncora é a chave de ordenação e o <c>Id</c> de desempate.</summary>
    public static AncorasDaPagina Ordenadas((string SortKey, Guid Id)? anterior, (string SortKey, Guid Id)? proxima) =>
        new(DaChaveOrdenada(anterior), DaChaveOrdenada(proxima));

    private static AncoraDoCursor? DoId(Guid? id) =>
        id is { } valor ? new AncoraDoCursor(valor.ToString(), SortKey: null) : null;

    private static AncoraDoCursor? DaChaveOrdenada((string SortKey, Guid Id)? ancora) =>
        ancora is { } valor ? new AncoraDoCursor(valor.Id.ToString(), valor.SortKey) : null;
}
