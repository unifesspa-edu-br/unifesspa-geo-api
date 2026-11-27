namespace Unifesspa.Geo.IntegrationTests.Paginacao;

/// <summary>
/// Endereço absoluto de um link de navegação, com os parâmetros da consulta na ordem em que
/// aparecem e já decodificados.
/// </summary>
internal sealed class EnderecoDeNavegacao
{
    private readonly Uri _endereco;

    public EnderecoDeNavegacao(string texto)
    {
        Texto = texto;
        _endereco = new Uri(texto, UriKind.Absolute);
        Parametros = [.. _endereco.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(LerParametro)];
    }

    public string Texto { get; }

    public string Caminho => _endereco.AbsolutePath;

    public IReadOnlyList<KeyValuePair<string, string>> Parametros { get; }

    public IReadOnlyList<string> Nomes => [.. Parametros.Select(parametro => parametro.Key)];

    public string? Valor(string nome) =>
        Parametros.Where(parametro => string.Equals(parametro.Key, nome, StringComparison.Ordinal)).Select(parametro => parametro.Value).SingleOrDefault();

    private static KeyValuePair<string, string> LerParametro(string texto)
    {
        string[] partes = texto.Split('=', 2);
        return new(Uri.UnescapeDataString(partes[0]), Uri.UnescapeDataString(partes[1]));
    }
}
