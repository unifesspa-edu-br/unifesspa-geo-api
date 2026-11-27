namespace Unifesspa.Geo.Infrastructure.Core;

using Microsoft.Extensions.Options;

/// <summary>
/// Os validadores de opções do Geo conferem só a instância padrão: o projeto não usa opções
/// nomeadas, e a validação de uma instância com outro nome não teria o que conferir.
/// </summary>
internal static class InstanciaPadraoDasOpcoes
{
    internal static bool Ignora(string? nome) =>
        !string.IsNullOrEmpty(nome) && !string.Equals(nome, Options.DefaultName, StringComparison.Ordinal);
}
