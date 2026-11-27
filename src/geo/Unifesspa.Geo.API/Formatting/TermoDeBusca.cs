namespace Unifesspa.Geo.API.Formatting;

using Unifesspa.Geo.API.Errors;
using Unifesspa.Geo.Kernel.Results;

/// <summary>Limite defensivo do termo de busca <c>q</c> (cobre o nome máximo com margem).</summary>
internal static class TermoDeBusca
{
    public const int TamanhoMaximo = 256;

    public static DomainError? Validar(string? termo) =>
        termo is { Length: > TamanhoMaximo } ? ErrosDasConsultas.ErroTermoDeBuscaMuitoLongo() : null;
}
