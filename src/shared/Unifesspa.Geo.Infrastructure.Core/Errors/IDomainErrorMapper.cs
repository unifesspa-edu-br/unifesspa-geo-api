namespace Unifesspa.Geo.Infrastructure.Core.Errors;

using System.Diagnostics.CodeAnalysis;

public interface IDomainErrorMapper
{
    bool TryGetMapping(string code, [MaybeNullWhen(false)] out DomainErrorMapping mapping);

    /// <summary>
    /// Endereço da página do portal que explica <paramref name="code"/>: o campo <c>type</c>
    /// do corpo de erro. Deriva do código, e não do registro.
    /// </summary>
    Uri GetProblemTypeUri(string code);
}
