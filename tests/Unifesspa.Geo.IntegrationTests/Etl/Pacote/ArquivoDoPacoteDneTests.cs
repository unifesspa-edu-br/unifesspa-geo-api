namespace Unifesspa.Geo.IntegrationTests.Etl.Pacote;

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

using AwesomeAssertions;

using Unifesspa.Geo.Domain.Errors;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;
using Unifesspa.Geo.Kernel.Results;

/// <summary>Gravação do pacote com limite real de bytes, resumo SHA-256, conferência do zip e remoção do arquivo.</summary>
public sealed class ArquivoDoPacoteDneTests : IDisposable
{
    private readonly string _diretorio = Path.Combine(Path.GetTempPath(), $"pacote-dne-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_diretorio))
        {
            Directory.Delete(_diretorio, recursive: true);
        }
    }

    [Fact(DisplayName = "Pacote válido é gravado com o resumo SHA-256, o tamanho e a versão do conteúdo")]
    public async Task PacoteValido_EhGravadoEConferido()
    {
        byte[] zip = MontarZip(EntradasDoPacote("202610"));
        Guid execucao = Guid.NewGuid();

        PacoteDneRecebido recebido = await ReceberComSucessoAsync(zip, execucao, zip.Length);

        recebido.Versao.Should().Be("202610");
        recebido.TamanhoEmBytes.Should().Be(zip.Length);
        recebido.Sha256.Should().Be(Convert.ToHexStringLower(SHA256.HashData(zip)));
        recebido.Caminho.Should().Be(Path.Combine(_diretorio, execucao.ToString("N")));
        (await File.ReadAllBytesAsync(recebido.Caminho)).Should().Equal(zip);
    }

    [Fact(DisplayName = "O arquivo é gravado com permissão 0600")]
    public async Task Arquivo_TemPermissao0600()
    {
        byte[] zip = MontarZip(EntradasDoPacote("202610"));

        PacoteDneRecebido recebido = await ReceberComSucessoAsync(zip, Guid.NewGuid(), zip.Length);

        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(recebido.Caminho).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact(DisplayName = "Corpo acima do limite real de bytes é recusado e o arquivo é apagado")]
    public async Task CorpoAcimaDoLimite_ApagaOArquivo()
    {
        byte[] zip = MontarZip(EntradasDoPacote("202610"));
        Guid execucao = Guid.NewGuid();

        Result<PacoteDneRecebido> resultado = await ReceberAsync(zip, execucao, zip.Length - 1);

        resultado.Error!.Code.Should().Be(GeoImportacaoErrorCodes.PacoteAcimaDoLimite);
        File.Exists(Path.Combine(_diretorio, execucao.ToString("N"))).Should().BeFalse();
    }

    [Fact(DisplayName = "Corpo no limite exato é aceito")]
    public async Task CorpoNoLimite_EhAceito()
    {
        byte[] zip = MontarZip(EntradasDoPacote("202610"));

        PacoteDneRecebido recebido = await ReceberComSucessoAsync(zip, Guid.NewGuid(), zip.Length);

        recebido.TamanhoEmBytes.Should().Be(zip.Length);
    }

    [Fact(DisplayName = "Arquivo que não é zip é recusado como pacote inválido e apagado")]
    public async Task ArquivoQueNaoEhZip_EhRecusadoEApagado()
    {
        Guid execucao = Guid.NewGuid();

        Result<PacoteDneRecebido> resultado = await ReceberAsync(Encoding.UTF8.GetBytes("não sou um zip"), execucao, 1024);

        resultado.Error!.Code.Should().Be(GeoImportacaoErrorCodes.PacoteInvalido);
        File.Exists(Path.Combine(_diretorio, execucao.ToString("N"))).Should().BeFalse();
    }

    [Fact(DisplayName = "Zip com tabela faltando é recusado com a falta citada e o arquivo é apagado")]
    public async Task ZipComTabelaFaltando_EhRecusadoEApagado()
    {
        Dictionary<string, byte[]> entradas = EntradasDoPacote("202610");
        entradas.Remove(NomeDaTabelaDne.Entrada("202610", "estado"));
        Guid execucao = Guid.NewGuid();

        Result<PacoteDneRecebido> resultado = await ReceberAsync(MontarZip(entradas), execucao, 1_000_000);

        resultado.Error!.Message.Should().Contain("ausentes: estado");
        File.Exists(Path.Combine(_diretorio, execucao.ToString("N"))).Should().BeFalse();
    }

    [Fact(DisplayName = "Zip com a imagem 0relacionamentos.jpg é aceito e a entrada de uma tabela abre para leitura")]
    public async Task ZipComImagem_EhAceitoEEntradaAbre()
    {
        Dictionary<string, byte[]> entradas = EntradasDoPacote("202610");
        entradas["0relacionamentos.jpg"] = [1, 2, 3];
        byte[] zip = MontarZip(entradas);
        PacoteDneRecebido recebido = await ReceberComSucessoAsync(zip, Guid.NewGuid(), zip.Length);

        using ZipArchive aberto = ArquivoDoPacoteDne.AbrirZip(recebido.Caminho);
        await using Stream entrada = await ArquivoDoPacoteDne.AbrirEntradaAsync(aberto, "202610", "cidade_faixa", CancellationToken.None);
        List<object?[]> linhas = await LeituraDoArquivoSqlDne.LerTudoAsync(entrada, "cidade_faixa", versao: "202610");

        linhas.Should().ContainSingle();
    }

    [Fact(DisplayName = "Ao apagar, o arquivo some")]
    public async Task Apagar_RemoveOArquivo()
    {
        byte[] zip = MontarZip(EntradasDoPacote("202610"));
        PacoteDneRecebido recebido = await ReceberComSucessoAsync(zip, Guid.NewGuid(), zip.Length);

        ArquivoDoPacoteDne.Apagar(recebido.Caminho);

        File.Exists(recebido.Caminho).Should().BeFalse();
    }

    [Fact(DisplayName = "Cancelamento durante a gravação apaga o arquivo")]
    public async Task CancelamentoDuranteAGravacao_ApagaOArquivo()
    {
        Guid execucao = Guid.NewGuid();
        using CancellationTokenSource cancelada = new();
        await cancelada.CancelAsync();
        ArquivoDoPacoteDne arquivo = new(_diretorio);

        Func<Task> acao = async () =>
        {
            using MemoryStream corpo = new([1, 2, 3]);
            await arquivo.ReceberAsync(corpo, execucao, 1024, cancelada.Token);
        };

        await acao.Should().ThrowAsync<OperationCanceledException>();
        File.Exists(Path.Combine(_diretorio, execucao.ToString("N"))).Should().BeFalse();
    }

    private static Dictionary<string, byte[]> EntradasDoPacote(string versao) =>
        TabelasDne.Nomes.ToDictionary(
            tabela => NomeDaTabelaDne.Entrada(versao, tabela),
            tabela => ConteudoDaTabela(tabela, versao));

    private static byte[] ConteudoDaTabela(string tabela, string versao)
    {
        List<string> linhas = ArquivoSqlDneSintetico.ComFechamentoEmUmaLinha(tabela, versao);
        linhas.Add(ArquivoSqlDneSintetico.Insert(ArquivoSqlDneSintetico.ValoresCompativeis(tabela), tabela, versao));
        return ArquivoSqlDneSintetico.Fluxo(linhas).ToArray();
    }

    private static byte[] MontarZip(Dictionary<string, byte[]> entradas)
    {
        using MemoryStream saida = new();
        using (ZipArchive zip = new(saida, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string nome, byte[] conteudo) in entradas)
            {
                using Stream entrada = zip.CreateEntry(nome).Open();
                entrada.Write(conteudo);
            }
        }

        return saida.ToArray();
    }

    private async Task<Result<PacoteDneRecebido>> ReceberAsync(byte[] corpo, Guid execucao, long limite)
    {
        using MemoryStream fluxo = new(corpo);
        return await new ArquivoDoPacoteDne(_diretorio).ReceberAsync(fluxo, execucao, limite, CancellationToken.None);
    }

    private async Task<PacoteDneRecebido> ReceberComSucessoAsync(byte[] corpo, Guid execucao, long limite)
    {
        Result<PacoteDneRecebido> resultado = await ReceberAsync(corpo, execucao, limite);
        resultado.IsSuccess.Should().BeTrue();
        return resultado.Value!;
    }
}
