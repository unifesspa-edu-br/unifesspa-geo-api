namespace Unifesspa.Geo.IntegrationTests.Etl.Pacote;

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

using AwesomeAssertions;

using Unifesspa.Geo.Application.DTOs;
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

    [Fact(DisplayName = "Formato de 202601: tabelas no topo e imagem jpg são aceitos e a entrada de uma tabela abre para leitura")]
    public async Task ZipNoTopoComImagemJpg_EhAceitoEEntradaAbre()
    {
        Dictionary<string, byte[]> entradas = EntradasDoPacote("202601");
        entradas["0relacionamentos.jpg"] = [1, 2, 3];
        byte[] zip = MontarZip(entradas);
        PacoteDneRecebido recebido = await ReceberComSucessoAsync(zip, Guid.NewGuid(), zip.Length);

        await ConferirQueAEntradaAbreAsync(recebido, "202601");
    }

    [Fact(DisplayName = "Formato de 202610: entrada da pasta, imagem png e tabelas dentro da pasta são aceitos e a carga abre a tabela na pasta")]
    public async Task ZipNaPastaComEntradaDePastaEPng_EhAceitoEEntradaAbre()
    {
        byte[] zip = MontarZip(EntradasNaPasta("202610", "qualquer_pasta-1", comEntradaDePasta: true));
        PacoteDneRecebido recebido = await ReceberComSucessoAsync(zip, Guid.NewGuid(), zip.Length);

        recebido.Versao.Should().Be("202610");
        await ConferirQueAEntradaAbreAsync(recebido, "202610");
    }

    [Fact(DisplayName = "Pasta sem a entrada da própria pasta é aceita")]
    public async Task ZipNaPastaSemEntradaDePasta_EhAceito()
    {
        byte[] zip = MontarZip(EntradasNaPasta("202610", "postgresql", comEntradaDePasta: false));

        PacoteDneRecebido recebido = await ReceberComSucessoAsync(zip, Guid.NewGuid(), zip.Length);

        await ConferirQueAEntradaAbreAsync(recebido, "202610");
    }

    [Fact(DisplayName = "Mistura de tabelas no topo e na pasta é recusada no recebimento e o arquivo é apagado")]
    public async Task ZipComTopoEPasta_EhRecusadoEApagado()
    {
        Dictionary<string, byte[]> entradas = EntradasNaPasta("202610", "postgresql", comEntradaDePasta: true);
        entradas[NomeDaTabelaDne.Entrada("202610", "estado")] = [];
        entradas.Remove("postgresql/" + NomeDaTabelaDne.Entrada("202610", "estado"));
        Guid execucao = Guid.NewGuid();

        Result<PacoteDneRecebido> resultado = await ReceberAsync(MontarZip(entradas), execucao, 5_000_000);

        resultado.Error!.Message.Should().Contain("mesma pasta");
        File.Exists(Path.Combine(_diretorio, execucao.ToString("N"))).Should().BeFalse();
    }

    [Fact(DisplayName = "Entradas acima do limite configurado são recusadas no recebimento")]
    public async Task EntradasAcimaDoLimiteConfigurado_SaoRecusadas()
    {
        byte[] zip = MontarZip(EntradasNaPasta("202610", "postgresql", comEntradaDePasta: true));
        using MemoryStream fluxo = new(zip);

        Result<PacoteDneRecebido> resultado = await new ArquivoDoPacoteDne(_diretorio, quantidadeMaximaDeEntradas: 16)
            .ReceberAsync(fluxo, Guid.NewGuid(), zip.Length, CancellationToken.None);

        resultado.Error!.Message.Should().Contain("mais de 16 entradas");
    }

    [Fact(DisplayName = "Abrir a entrada de uma tabela que o zip não tem falha com erro de operação")]
    public async Task EntradaAusente_Falha()
    {
        Dictionary<string, byte[]> entradas = EntradasDoPacote("202610");
        byte[] zip = MontarZip(entradas);
        PacoteDneRecebido recebido = await ReceberComSucessoAsync(zip, Guid.NewGuid(), zip.Length);
        using ZipArchive aberto = ArquivoDoPacoteDne.AbrirZip(recebido.Caminho);

        Func<Task> abertura = async () => await ArquivoDoPacoteDne.AbrirEntradaAsync(aberto, "202601", "cidade_faixa", CancellationToken.None);

        await abertura.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact(DisplayName = "Ao apagar, o arquivo some")]
    public async Task Apagar_RemoveOArquivo()
    {
        byte[] zip = MontarZip(EntradasDoPacote("202610"));
        PacoteDneRecebido recebido = await ReceberComSucessoAsync(zip, Guid.NewGuid(), zip.Length);

        ArquivoDoPacoteDne.Apagar(recebido.Caminho);

        File.Exists(recebido.Caminho).Should().BeFalse();
    }

    [Fact(DisplayName = "Falha antes de criar o arquivo chega como a exceção original, sem erro de remoção por cima")]
    public async Task FalhaAntesDeCriarOArquivo_PreservaAExcecaoOriginal()
    {
        Directory.CreateDirectory(_diretorio);
        string diretorioQueEUmArquivo = Path.Combine(_diretorio, "ocupado");
        await File.WriteAllTextAsync(diretorioQueEUmArquivo, "arquivo comum");

        Func<Task> recebimento = async () =>
        {
            using MemoryStream corpo = new([1, 2, 3]);
            await new ArquivoDoPacoteDne(diretorioQueEUmArquivo).ReceberAsync(corpo, Guid.NewGuid(), 1024, CancellationToken.None);
        };

        (await recebimento.Should().ThrowAsync<IOException>()).Which.Should().NotBeOfType<DirectoryNotFoundException>();
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

    private static Dictionary<string, byte[]> EntradasNaPasta(string versao, string pasta, bool comEntradaDePasta)
    {
        Dictionary<string, byte[]> entradas = [];
        if (comEntradaDePasta)
        {
            entradas[$"{pasta}/"] = [];
            entradas[$"{pasta}/0relacionamentos.png"] = [1, 2, 3];
        }

        foreach ((string nome, byte[] conteudo) in EntradasDoPacote(versao))
        {
            entradas[$"{pasta}/{nome}"] = conteudo;
        }

        return entradas;
    }

    private static async Task ConferirQueAEntradaAbreAsync(PacoteDneRecebido recebido, string versao)
    {
        using ZipArchive aberto = ArquivoDoPacoteDne.AbrirZip(recebido.Caminho);
        await using Stream entrada = await ArquivoDoPacoteDne.AbrirEntradaAsync(aberto, versao, "cidade_faixa", CancellationToken.None);
        List<object?[]> linhas = await LeituraDoArquivoSqlDne.LerTudoAsync(entrada, "cidade_faixa", versao: versao);

        linhas.Should().ContainSingle();
    }

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
