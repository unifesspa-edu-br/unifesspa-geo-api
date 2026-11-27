namespace Unifesspa.Geo.IntegrationTests.TestSupport.CatalogoDeErros;

/// <summary>Resume, em linhas curtas, o que mudou entre o catálogo publicado e o gerado.</summary>
internal static class DivergenciasDoCatalogoDeErros
{
    internal static IReadOnlyList<string> Comparar(
        IReadOnlyList<EntradaDoCatalogoDeErros> publicado,
        IReadOnlyList<EntradaDoCatalogoDeErros> gerado)
    {
        Dictionary<string, EntradaDoCatalogoDeErros> antes = publicado.ToDictionary(static e => e.Codigo, StringComparer.Ordinal);
        Dictionary<string, EntradaDoCatalogoDeErros> depois = gerado.ToDictionary(static e => e.Codigo, StringComparer.Ordinal);
        return
        [
            .. depois.Keys.Except(antes.Keys, StringComparer.Ordinal).Select(c => $"Código novo: {c}."),
            .. antes.Keys.Except(depois.Keys, StringComparer.Ordinal).Select(c => $"Código removido: {c}."),
            .. depois.Where(par => antes.TryGetValue(par.Key, out EntradaDoCatalogoDeErros? anterior) && anterior != par.Value)
                .Select(par => $"Código alterado: {par.Key}, de {antes[par.Key].Descricao} para {par.Value.Descricao}."),
        ];
    }
}
