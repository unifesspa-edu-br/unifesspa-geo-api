namespace Unifesspa.Geo.Infrastructure.Core.Authentication;

using System.Text.Json;

/// <summary>
/// Leitura única do array <c>roles</c> dentro de um claim JSON do Keycloak
/// (<c>realm_access</c> ou um recurso de <c>resource_access</c>). Cada chamador mantém a
/// própria política para claim malformado.
/// </summary>
internal static class PapeisDoKeycloak
{
    /// <summary>
    /// Lê os papéis do claim. Devolve nulo se o JSON for inválido ou se a raiz não for um objeto;
    /// lista vazia se o claim estiver em branco ou faltar o recurso ou o array; e só os elementos
    /// que são texto — um elemento de outro tipo faria <c>GetString()</c> lançar
    /// <see cref="InvalidOperationException"/>, que viraria erro 500.
    /// </summary>
    /// <param name="json">Valor do claim.</param>
    /// <param name="recurso">Nome do recurso dentro de <c>resource_access</c>; nulo para <c>realm_access</c>.</param>
    /// <param name="falha">Exceção do analisador de JSON quando o JSON é inválido; nula nos demais casos.</param>
    internal static string[]? Ler(string? json, string? recurso, out JsonException? falha)
    {
        falha = null;
        return string.IsNullOrWhiteSpace(json) ? [] : Analisar(json, recurso, out falha);
    }

    private static string[]? Analisar(string json, string? recurso, out JsonException? falha)
    {
        falha = null;
        try
        {
            using JsonDocument documento = JsonDocument.Parse(json);
            JsonElement raiz = documento.RootElement;
            return raiz.ValueKind == JsonValueKind.Object ? PapeisDoEscopo(raiz, recurso) : null;
        }
        catch (JsonException ex)
        {
            falha = ex;
            return null;
        }
    }

    private static string[] PapeisDoEscopo(JsonElement raiz, string? recurso)
    {
        if (!TentarEscopo(raiz, recurso, out JsonElement escopo)
            || !escopo.TryGetProperty(KeycloakClaims.Roles, out JsonElement papeis)
            || papeis.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. papeis.EnumerateArray()
            .Where(static papel => papel.ValueKind == JsonValueKind.String)
            .Select(static papel => papel.GetString())
            .OfType<string>()];
    }

    private static bool TentarEscopo(JsonElement raiz, string? recurso, out JsonElement escopo)
    {
        escopo = raiz;
        return recurso is null || (raiz.TryGetProperty(recurso, out escopo) && escopo.ValueKind == JsonValueKind.Object);
    }
}
