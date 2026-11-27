namespace Unifesspa.Geo.IntegrationTests.TestSupport.CatalogoDeErros;

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>O conteúdo de <c>contracts/descricoes-de-erros.json</c>, em formato canônico ordenado por código.</summary>
internal sealed record DocumentoDeDescricoesDeErros(int VersaoDoFormato, IReadOnlyList<DescricaoDeErro?>? Descricoes)
{
    internal const int VersaoAtual = 1;

    private static readonly JsonSerializerOptions Opcoes = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        NumberHandling = JsonNumberHandling.Strict,
    };

    internal static DocumentoDeDescricoesDeErros Ler(string conteudo) =>
        JsonSerializer.Deserialize<DocumentoDeDescricoesDeErros>(conteudo, Opcoes)
        ?? throw new JsonException("Falta o documento de descrições.");

    internal string SerializarEmOrdem()
    {
        DocumentoDeDescricoesDeErros ordenado = this with
        {
            Descricoes = Descricoes?.OrderBy(static descricao => descricao?.Codigo, StringComparer.Ordinal).ToArray(),
        };
        return JsonSerializer.Serialize(ordenado, Opcoes) + "\n";
    }
}
