namespace Unifesspa.Geo.IntegrationTests.TestSupport.CatalogoDeErros;

using System.Text.Encodings.Web;
using System.Text.Json;

/// <summary>
/// O conteúdo de <c>contracts/codigos-de-erro.json</c>: a versão do formato e a lista de códigos,
/// ordenada por código.
/// </summary>
internal sealed record DocumentoDoCatalogoDeErros(int VersaoDoFormato, IReadOnlyList<EntradaDoCatalogoDeErros> Codigos)
{
    internal const int VersaoAtualDoFormato = 1;

    // O encoder relaxado mantém os acentos do título legíveis no arquivo, em vez de \uXXXX.
    private static readonly JsonSerializerOptions Opcoes = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Reúne as entradas em ordem de código. O mesmo código declarado mais de uma vez entra uma vez
    /// se o status e o título forem iguais; se forem diferentes, a geração é recusada.
    /// </summary>
    internal static DocumentoDoCatalogoDeErros Gerar(IEnumerable<EntradaDoCatalogoDeErros> entradas)
    {
        ArgumentNullException.ThrowIfNull(entradas);
        EntradaDoCatalogoDeErros[] unicas =
        [
            .. entradas.GroupBy(static e => e.Codigo, StringComparer.Ordinal)
                .OrderBy(static g => g.Key, StringComparer.Ordinal)
                .Select(static g => UnirDuplicadas(g.Key, g.ToArray())),
        ];
        return new DocumentoDoCatalogoDeErros(VersaoAtualDoFormato, unicas);
    }

    internal static DocumentoDoCatalogoDeErros Ler(string json) =>
        JsonSerializer.Deserialize<DocumentoDoCatalogoDeErros>(json, Opcoes)
        ?? throw new JsonException("O arquivo de códigos de erro está vazio.");

    internal string Serializar() => JsonSerializer.Serialize(this, Opcoes) + "\n";

    private static EntradaDoCatalogoDeErros UnirDuplicadas(string codigo, EntradaDoCatalogoDeErros[] declaracoes)
    {
        EntradaDoCatalogoDeErros primeira = declaracoes[0];
        EntradaDoCatalogoDeErros? diferente = declaracoes.FirstOrDefault(e => e != primeira);
        return diferente is null
            ? primeira
            : throw new InvalidOperationException(
                $"O código {codigo} é declarado com {primeira.Descricao} e com {diferente.Descricao}.");
    }
}
