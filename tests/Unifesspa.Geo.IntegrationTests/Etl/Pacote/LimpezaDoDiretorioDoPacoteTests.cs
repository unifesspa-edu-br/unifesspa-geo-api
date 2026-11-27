namespace Unifesspa.Geo.IntegrationTests.Etl.Pacote;

using AwesomeAssertions;

using Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

/// <summary>Limpeza do diretório de pacotes na partida e remoção do arquivo de uma execução.</summary>
public sealed class LimpezaDoDiretorioDoPacoteTests : IDisposable
{
    private readonly string _diretorio = Path.Combine(Path.GetTempPath(), $"pacote-dne-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_diretorio))
        {
            Directory.Delete(_diretorio, recursive: true);
        }
    }

    [Fact(DisplayName = "Limpar o diretório apaga arquivos e subpastas e conta o que removeu")]
    public void LimparDiretorio_ApagaTudo()
    {
        Directory.CreateDirectory(Path.Combine(_diretorio, "subpasta"));
        File.WriteAllText(Path.Combine(_diretorio, "subpasta", "interno"), "x");
        File.WriteAllText(Path.Combine(_diretorio, Guid.NewGuid().ToString("N")), "x");
        File.WriteAllText(Path.Combine(_diretorio, Guid.NewGuid().ToString("N")), "x");

        int removidos = new ArquivoDoPacoteDne(_diretorio).LimparDiretorio();

        removidos.Should().Be(3);
        Directory.EnumerateFileSystemEntries(_diretorio).Should().BeEmpty();
    }

    [Fact(DisplayName = "Limpar um diretório que não existe não faz nada")]
    public void LimparDiretorio_SemDiretorio_DevolveZero()
    {
        new ArquivoDoPacoteDne(_diretorio).LimparDiretorio().Should().Be(0);
        Directory.Exists(_diretorio).Should().BeFalse();
    }

    [Fact(DisplayName = "Apagar o arquivo da execução remove só o dela e aceita arquivo ou diretório ausente")]
    public void ApagarSeExistir_RemoveSoOArquivoDaExecucao()
    {
        ArquivoDoPacoteDne arquivos = new(_diretorio);
        Guid execucao = Guid.NewGuid();
        Guid outra = Guid.NewGuid();
        arquivos.ApagarSeExistir(execucao);

        Directory.CreateDirectory(_diretorio);
        File.WriteAllText(arquivos.CaminhoDe(execucao), "x");
        File.WriteAllText(arquivos.CaminhoDe(outra), "x");
        arquivos.ApagarSeExistir(execucao);
        arquivos.ApagarSeExistir(execucao);

        File.Exists(arquivos.CaminhoDe(execucao)).Should().BeFalse();
        File.Exists(arquivos.CaminhoDe(outra)).Should().BeTrue();
    }
}
