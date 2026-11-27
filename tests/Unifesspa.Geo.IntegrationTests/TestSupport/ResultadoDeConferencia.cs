namespace Unifesspa.Geo.IntegrationTests.TestSupport;

using System.Text;

/// <summary>
/// Guarda o resultado obtido de uma conferência em pasta temporária, sem alterar o arquivo
/// versionado, e monta a mensagem que informa o comando de aprovação.
/// </summary>
public static class ResultadoDeConferencia
{
    private const string PastaRaiz = "unifesspa-geo-resultados";

    public static string Gravar(string nome, string conteudo)
    {
        ArgumentNullException.ThrowIfNull(conteudo);
        string nomeDoArquivo = Path.GetFileName(nome);
        string pasta = Path.Join(Path.GetTempPath(), PastaRaiz, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        string caminho = Path.Join(pasta, nomeDoArquivo);
        File.WriteAllText(caminho, conteudo, new UTF8Encoding(false));
        return caminho;
    }

    public static string Mensagem(string resultado, string arquivoVersionado)
    {
        ArgumentNullException.ThrowIfNull(resultado);
        ArgumentNullException.ThrowIfNull(arquivoVersionado);
        return $"resultado obtido: {resultado}\n" +
            $"para ver a diferença: git diff --no-index \"{Escapar(arquivoVersionado)}\" \"{Escapar(resultado)}\"\n" +
            $"para aprovar, a partir da raiz do repositório: cp \"{Escapar(resultado)}\" \"{Escapar(arquivoVersionado)}\"";
    }

    private static string Escapar(string caminho) => caminho
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal)
        .Replace("$", "\\$", StringComparison.Ordinal)
        .Replace("`", "\\`", StringComparison.Ordinal);
}
