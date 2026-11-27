namespace Unifesspa.Geo.IntegrationTests.Cifragem;

/// <summary>Arquivo no diretório temporário do sistema, apagado ao descartar.</summary>
internal sealed class ArquivoTemporarioDeTeste : IDisposable
{
    private ArquivoTemporarioDeTeste(string caminho) => Caminho = caminho;

    public string Caminho { get; }

    public static ArquivoTemporarioDeTeste ComConteudo(string conteudo)
    {
        ArquivoTemporarioDeTeste arquivo = Inexistente();
        File.WriteAllText(arquivo.Caminho, conteudo);
        return arquivo;
    }

    public static ArquivoTemporarioDeTeste Inexistente() =>
        new(Path.Combine(Path.GetTempPath(), $"geo-cifragem-{Guid.NewGuid():N}.jwt"));

    public void Dispose()
    {
        if (File.Exists(Caminho))
        {
            File.Delete(Caminho);
        }
    }
}
