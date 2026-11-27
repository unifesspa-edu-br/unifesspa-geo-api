namespace Unifesspa.Geo.IntegrationTests.Etl;

using Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

/// <summary>Diretório de pacotes próprio do teste, removido ao fim, que sabe deixar um arquivo residual.</summary>
internal sealed class DiretorioDePacotesTemporario : IDisposable
{
    private readonly string _caminho = Path.Combine(Path.GetTempPath(), $"pacotes-dne-{Guid.NewGuid():N}");

    public DiretorioDePacotesTemporario()
    {
        Arquivos = new ArquivoDoPacoteDne(_caminho);
    }

    public ArquivoDoPacoteDne Arquivos { get; }

    public bool EstaVazio => !Directory.Exists(_caminho) || !Directory.EnumerateFileSystemEntries(_caminho).Any();

    public async Task DeixarArquivoResidualAsync()
    {
        Directory.CreateDirectory(_caminho);
        await File.WriteAllTextAsync(Arquivos.CaminhoDe(Guid.NewGuid()), "resíduo");
    }

    public void Dispose()
    {
        if (Directory.Exists(_caminho))
        {
            Directory.Delete(_caminho, recursive: true);
        }
    }
}
