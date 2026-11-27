namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

using System.Buffers;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;

using Unifesspa.Geo.Domain.Errors;
using Unifesspa.Geo.Infrastructure.Persistence.Etl;
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
    private readonly int _quantidadeMaximaDeEntradas;
    private readonly long _tamanhoMaximoEmBytes;

    public ArquivoDoPacoteDne(
        string diretorio,
        int quantidadeMaximaDeEntradas = EtlOpcoes.QuantidadePadraoDeEntradasDoPacote,
        long tamanhoMaximoEmBytes = EtlOpcoes.TamanhoPadraoDoPacote)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diretorio);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tamanhoMaximoEmBytes);
        _diretorio = diretorio;
        _quantidadeMaximaDeEntradas = quantidadeMaximaDeEntradas;
        _tamanhoMaximoEmBytes = tamanhoMaximoEmBytes;
    }

    /// <summary>
    /// Grava o corpo e confere o zip. Em qualquer recusa, ou exceção, o arquivo é apagado.
    /// </summary>
    public async Task<Result<PacoteDneRecebido>> ReceberAsync(
        Stream corpo,
        Guid execucaoId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(corpo);
        string caminho = CaminhoDe(execucaoId);
        try
        {
            return await GravarEConferirAsync(corpo, caminho, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            ApagarSeExistir(caminho);
            throw;
        }
    }

    /// <summary>Abre o zip gravado para leitura; quem chama descarta o <see cref="ZipArchive"/>.</summary>
    public static ZipArchive AbrirZip(string caminho) => ZipFile.OpenRead(caminho);

    /// <summary>Abre o fluxo descompactado da entrada da tabela (no topo ou na pasta do pacote), ou falha se o zip não tiver exatamente uma.</summary>
    public static async Task<Stream> AbrirEntradaAsync(
        ZipArchive zip,
        string versao,
        string tabela,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(zip);
        string nome = NomeDaTabelaDne.Entrada(versao, tabela);
        ZipArchiveEntry[] candidatas = [.. zip.Entries.Where(e => string.Equals(e.Name, nome, StringComparison.Ordinal)).Take(2)];
        if (candidatas.Length != 1)
        {
            throw new InvalidOperationException("O pacote não tem uma única entrada da tabela.");
        }

        return await candidatas[0].OpenAsync(cancellationToken).ConfigureAwait(false);
    }

    public static void Apagar(string caminho) => File.Delete(caminho);

    /// <summary>Caminho do arquivo da execução: o nome é o identificador, nunca o nome enviado pelo cliente.</summary>
    public string CaminhoDe(Guid execucaoId) =>
        Path.Combine(_diretorio, execucaoId.ToString("N", CultureInfo.InvariantCulture));

    /// <summary>
    /// Passa o arquivo recebido a ser o da execução: renomeia-o para o identificador dela
    /// e devolve o novo caminho.
    /// </summary>
    public string AtribuirExecucao(string caminhoRecebido, Guid execucaoId)
    {
        string destino = CaminhoDe(execucaoId);
        File.Move(caminhoRecebido, destino);
        return destino;
    }

    /// <summary>Apaga o arquivo da execução se ele existir.</summary>
    public void ApagarSeExistir(Guid execucaoId) => ApagarSeExistir(CaminhoDe(execucaoId));

    /// <summary>Apaga o arquivo do caminho informado se ele existir.</summary>
    public static void ApagarSeExistir(string caminho)
    {
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
        CancellationToken cancellationToken)
    {
        FileStreamOptions opcoes = PrepararDiretorioEOpcoes();

        FileStream destino = new(caminho, opcoes);
        await using (destino.ConfigureAwait(false))
        {
            return await CopiarComResumoAsync(corpo, destino, cancellationToken).ConfigureAwait(false);
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
        CancellationToken cancellationToken)
    {
        ResumoDoCorpo? resumo = await GravarAsync(corpo, caminho, cancellationToken).ConfigureAwait(false);
        Result<PacoteDneRecebido> resultado = resumo is null
            ? Result<PacoteDneRecebido>.Failure(ErrosDoPacoteDne.PacoteAcimaDoLimite(_tamanhoMaximoEmBytes))
            : Conferir(caminho, resumo);
        if (resultado.IsFailure)
        {
            Apagar(caminho);
        }

        return resultado;
    }

    private async Task<ResumoDoCorpo?> CopiarComResumoAsync(
        Stream origem,
        Stream destino,
        CancellationToken cancellationToken)
    {
        using IncrementalHash resumo = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] bloco = ArrayPool<byte>.Shared.Rent(TamanhoDoBloco);
        try
        {
            long? total = await CopiarBlocosAsync(origem, destino, resumo, bloco, cancellationToken).ConfigureAwait(false);
            return total is null ? null : new ResumoDoCorpo(Convert.ToHexStringLower(resumo.GetHashAndReset()), total.Value);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bloco);
        }
    }

    private async Task<long?> CopiarBlocosAsync(
        Stream origem,
        Stream destino,
        IncrementalHash resumo,
        byte[] bloco,
        CancellationToken cancellationToken)
    {
        long total = 0;
        int lidos;
        while ((lidos = await origem.ReadAsync(bloco, cancellationToken).ConfigureAwait(false)) > 0)
        {
            total += lidos;
            if (total > _tamanhoMaximoEmBytes)
            {
                return null;
            }

            resumo.AppendData(bloco, 0, lidos);
            await destino.WriteAsync(bloco.AsMemory(0, lidos), cancellationToken).ConfigureAwait(false);
        }

        return total;
    }

    private Result<PacoteDneRecebido> Conferir(string caminho, ResumoDoCorpo resumo)
    {
        Result<IReadOnlyList<EntradaDoPacoteDne>> entradas = ListarEntradas(caminho);
        if (entradas.IsFailure)
        {
            return Result<PacoteDneRecebido>.Failure(entradas.Error!);
        }

        return ConferenciaDoPacoteDne.Conferir(entradas.Value!, _quantidadeMaximaDeEntradas).Match(
            versao => Result<PacoteDneRecebido>.Success(new PacoteDneRecebido(caminho, resumo.Sha256, resumo.Tamanho, versao)),
            Result<PacoteDneRecebido>.Failure);
    }

    private Result<IReadOnlyList<EntradaDoPacoteDne>> ListarEntradas(string caminho)
    {
        try
        {
            using FileStream arquivo = File.OpenRead(caminho);
            DomainError? recusa = FimDoDiretorioDoZip.Conferir(arquivo, _quantidadeMaximaDeEntradas);
            return recusa is not null
                ? Result<IReadOnlyList<EntradaDoPacoteDne>>.Failure(recusa)
                : Result<IReadOnlyList<EntradaDoPacoteDne>>.Success(EntradasDe(arquivo));
        }
        catch (InvalidDataException)
        {
            return Result<IReadOnlyList<EntradaDoPacoteDne>>.Failure(
                ErrosDoPacoteDne.PacoteInvalido("O arquivo enviado não é um zip válido."));
        }
    }

    private static List<EntradaDoPacoteDne> EntradasDe(Stream arquivo)
    {
        using ZipArchive zip = new(arquivo, ZipArchiveMode.Read, leaveOpen: true);
        return [.. zip.Entries.Select(e => new EntradaDoPacoteDne(e.FullName, e.Length, e.IsEncrypted))];
    }

    private sealed record ResumoDoCorpo(string Sha256, long Tamanho);
}
