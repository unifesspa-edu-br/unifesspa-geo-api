namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

using System.Buffers;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;

using Unifesspa.Geo.Kernel.Results;

/// <summary>
/// Guarda o pacote recebido no diretório configurado (volume em memória do pod): grava o corpo
/// com limite real de bytes e permissão 0600, em arquivo nomeado pelo identificador da execução,
/// calcula o resumo SHA-256 durante a gravação, confere o diretório do zip, abre uma entrada
/// para leitura e apaga o arquivo. O conteúdo não é cifrado no aplicativo.
/// </summary>
internal sealed class ArquivoDoPacoteDne
{
    private const int TamanhoDoBloco = 81920;

    private readonly string _diretorio;

    public ArquivoDoPacoteDne(string diretorio)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diretorio);
        _diretorio = diretorio;
    }

    /// <summary>
    /// Grava o corpo e confere o zip. Em qualquer recusa, ou exceção, o arquivo é apagado.
    /// </summary>
    public async Task<Result<PacoteDneRecebido>> ReceberAsync(
        Stream corpo,
        Guid execucaoId,
        long limiteDeBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(corpo);
        string caminho = CaminhoDe(execucaoId);
        try
        {
            return await GravarEConferirAsync(corpo, caminho, limiteDeBytes, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Apagar(caminho);
            throw;
        }
    }

    /// <summary>Abre o zip gravado para leitura; quem chama descarta o <see cref="ZipArchive"/>.</summary>
    public static ZipArchive AbrirZip(string caminho) => ZipFile.OpenRead(caminho);

    /// <summary>Abre o fluxo descompactado da entrada da tabela, ou falha se o zip não a tiver.</summary>
    public static async Task<Stream> AbrirEntradaAsync(
        ZipArchive zip,
        string versao,
        string tabela,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(zip);
        ZipArchiveEntry entrada = zip.GetEntry(NomeDaTabelaDne.Entrada(versao, tabela))
            ?? throw new InvalidOperationException("O pacote não tem a entrada da tabela.");
        return await entrada.OpenAsync(cancellationToken).ConfigureAwait(false);
    }

    public static void Apagar(string caminho) => File.Delete(caminho);

    /// <summary>Caminho do arquivo da execução: o nome é o identificador, nunca o nome enviado pelo cliente.</summary>
    public string CaminhoDe(Guid execucaoId) =>
        Path.Combine(_diretorio, execucaoId.ToString("N", CultureInfo.InvariantCulture));

    /// <summary>Apaga o arquivo da execução se ele existir.</summary>
    public void ApagarSeExistir(Guid execucaoId)
    {
        string caminho = CaminhoDe(execucaoId);
        if (File.Exists(caminho))
        {
            Apagar(caminho);
        }
    }

    /// <summary>Apaga tudo o que houver no diretório de pacotes (resíduo de um processo anterior); devolve quantos itens removeu.</summary>
    public int LimparDiretorio()
    {
        if (!Directory.Exists(_diretorio))
        {
            return 0;
        }

        string[] itens = [.. Directory.EnumerateFileSystemEntries(_diretorio)];
        foreach (string item in itens)
        {
            ApagarItem(item);
        }

        return itens.Length;
    }

    private static void ApagarItem(string item)
    {
        if (Directory.Exists(item))
        {
            Directory.Delete(item, recursive: true);
            return;
        }

        File.Delete(item);
    }

    private async Task<ResumoDoCorpo?> GravarAsync(
        Stream corpo,
        string caminho,
        long limiteDeBytes,
        CancellationToken cancellationToken)
    {
        FileStreamOptions opcoes = PrepararDiretorioEOpcoes();

        FileStream destino = new(caminho, opcoes);
        await using (destino.ConfigureAwait(false))
        {
            return await CopiarComResumoAsync(corpo, destino, limiteDeBytes, cancellationToken).ConfigureAwait(false);
        }
    }

    private FileStreamOptions PrepararDiretorioEOpcoes()
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("O recebimento do pacote exige permissão de arquivo Unix.");
        }

        Directory.CreateDirectory(_diretorio, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
        };
    }

    private async Task<Result<PacoteDneRecebido>> GravarEConferirAsync(
        Stream corpo,
        string caminho,
        long limiteDeBytes,
        CancellationToken cancellationToken)
    {
        ResumoDoCorpo? resumo = await GravarAsync(corpo, caminho, limiteDeBytes, cancellationToken).ConfigureAwait(false);
        Result<PacoteDneRecebido> resultado = resumo is null
            ? Result<PacoteDneRecebido>.Failure(ErrosDoPacoteDne.PacoteAcimaDoLimite(limiteDeBytes))
            : Conferir(caminho, resumo);
        if (resultado.IsFailure)
        {
            Apagar(caminho);
        }

        return resultado;
    }

    private static async Task<ResumoDoCorpo?> CopiarComResumoAsync(
        Stream origem,
        Stream destino,
        long limiteDeBytes,
        CancellationToken cancellationToken)
    {
        using IncrementalHash resumo = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] bloco = ArrayPool<byte>.Shared.Rent(TamanhoDoBloco);
        try
        {
            long? total = await CopiarBlocosAsync(origem, destino, resumo, bloco, limiteDeBytes, cancellationToken).ConfigureAwait(false);
            return total is null ? null : new ResumoDoCorpo(Convert.ToHexStringLower(resumo.GetHashAndReset()), total.Value);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bloco);
        }
    }

    private static async Task<long?> CopiarBlocosAsync(
        Stream origem,
        Stream destino,
        IncrementalHash resumo,
        byte[] bloco,
        long limiteDeBytes,
        CancellationToken cancellationToken)
    {
        long total = 0;
        int lidos;
        while ((lidos = await origem.ReadAsync(bloco, cancellationToken).ConfigureAwait(false)) > 0)
        {
            total += lidos;
            if (total > limiteDeBytes)
            {
                return null;
            }

            resumo.AppendData(bloco, 0, lidos);
            await destino.WriteAsync(bloco.AsMemory(0, lidos), cancellationToken).ConfigureAwait(false);
        }

        return total;
    }

    private static Result<PacoteDneRecebido> Conferir(string caminho, ResumoDoCorpo resumo)
    {
        Result<IReadOnlyList<EntradaDoPacoteDne>> entradas = ListarEntradas(caminho);
        if (entradas.IsFailure)
        {
            return Result<PacoteDneRecebido>.Failure(entradas.Error!);
        }

        return ConferenciaDoPacoteDne.Conferir(entradas.Value!).Match(
            versao => Result<PacoteDneRecebido>.Success(new PacoteDneRecebido(caminho, resumo.Sha256, resumo.Tamanho, versao)),
            Result<PacoteDneRecebido>.Failure);
    }

    private static Result<IReadOnlyList<EntradaDoPacoteDne>> ListarEntradas(string caminho)
    {
        try
        {
            using ZipArchive zip = ZipFile.OpenRead(caminho);
            return Result<IReadOnlyList<EntradaDoPacoteDne>>.Success(
                [.. zip.Entries.Select(e => new EntradaDoPacoteDne(e.FullName, e.Length, e.IsEncrypted))]);
        }
        catch (InvalidDataException)
        {
            return Result<IReadOnlyList<EntradaDoPacoteDne>>.Failure(
                ErrosDoPacoteDne.PacoteInvalido("O arquivo enviado não é um zip válido."));
        }
    }

    private sealed record ResumoDoCorpo(string Sha256, long Tamanho);
}
