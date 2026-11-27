namespace Unifesspa.Geo.IntegrationTests.TestSupport;

/// <summary>Resolve caminhos relativos à raiz do repositório, onde está a solução.</summary>
public static class CaminhoNoRepositorio
{
    private const string ArquivoDaSolucao = "Unifesspa.Geo.slnx";

    public static string Resolver(string relativo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativo);
        if (Path.IsPathRooted(relativo))
        {
            throw new ArgumentException("O caminho deve ser relativo à raiz do repositório.", nameof(relativo));
        }

        return Path.Combine(LocalizarRaiz(), relativo);
    }

    private static string LocalizarRaiz()
    {
        string? atual = AppContext.BaseDirectory;
        while (atual is not null && !File.Exists(Path.Combine(atual, ArquivoDaSolucao)))
        {
            atual = Path.GetDirectoryName(atual);
        }

        return atual ?? throw new DirectoryNotFoundException(
            $"{ArquivoDaSolucao} não encontrado a partir de {AppContext.BaseDirectory}.");
    }
}
