namespace Unifesspa.Geo.Infrastructure.Core.Pagination;

/// <summary>
/// Âncora de continuação serializada no cursor: <paramref name="After"/> é sempre o <c>Id</c>;
/// <paramref name="SortKey"/> só existe no keyset ordenado (ADR-0094).
/// </summary>
internal readonly record struct AncoraDoCursor(string After, string? SortKey);
