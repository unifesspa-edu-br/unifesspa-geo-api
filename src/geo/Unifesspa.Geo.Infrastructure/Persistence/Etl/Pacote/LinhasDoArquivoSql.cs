namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

using System.Text;

/// <summary>
/// Lê um arquivo SQL linha a linha em UTF-8 estrito (BOM opcional, fim de linha CRLF ou LF),
/// recusando linha acima de 64 KiB e contando os bytes realmente descompactados.
/// </summary>
internal sealed class LinhasDoArquivoSql
{
    private const int TamanhoDoBloco = 16 * 1024;
    private const int FolgaDoBomEDoRetorno = 4;
    private static readonly UTF8Encoding Utf8Estrito = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly Stream _fluxo;
    private readonly string _tabela;
    private readonly long _limiteDeBytes;
    private readonly byte[] _bloco = new byte[TamanhoDoBloco];
    private readonly byte[] _linha = new byte[LimitesDoPacoteDne.BytesPorLinha + FolgaDoBomEDoRetorno];
    private int _posicao;
    private int _disponivel;
    private int _tamanhoDaLinha;
    private bool _terminouComQuebra;

    public LinhasDoArquivoSql(Stream fluxo, string tabela, long limiteDeBytes)
    {
        ArgumentNullException.ThrowIfNull(fluxo);
        _fluxo = fluxo;
        _tabela = tabela;
        _limiteDeBytes = limiteDeBytes;
    }

    /// <summary>Número da linha lida por último (a da leitura em curso, se ela falhar).</summary>
    public int NumeroDaLinha { get; private set; }

    /// <summary>
    /// Fim de linha realmente lido na última linha: <c>\r\n</c>, <c>\n</c>, ou vazio no fim do arquivo.
    /// </summary>
    public string FimDeLinha { get; private set; } = string.Empty;

    /// <summary>Quando definido, as recusas citam esta linha (a de início de um comando de várias linhas).</summary>
    public int? LinhaDoComando { get; set; }

    /// <summary>Bytes descompactados lidos do fluxo até aqui.</summary>
    public long BytesLidos { get; private set; }

    /// <summary>Próxima linha sem o fim de linha, ou <see langword="null"/> no fim do arquivo.</summary>
    public async ValueTask<string?> LerLinhaAsync(CancellationToken cancellationToken)
    {
        _tamanhoDaLinha = 0;
        _terminouComQuebra = false;
        NumeroDaLinha++;
        while (true)
        {
            if (_posicao == _disponivel && !await PreencherAsync(cancellationToken).ConfigureAwait(false))
            {
                return TerminarNoFimDoArquivo();
            }

            if (AcumularAteAQuebra())
            {
                return Concluir();
            }
        }
    }

    public ErroDeLeituraDoPacoteDneException Recusa(string motivo) => Recusa(motivo, LinhaDoComando ?? NumeroDaLinha);

    public ErroDeLeituraDoPacoteDneException Recusa(string motivo, int numeroDaLinha) => new(_tabela, numeroDaLinha, motivo);

    private async ValueTask<bool> PreencherAsync(CancellationToken cancellationToken)
    {
        _posicao = 0;
        _disponivel = await _fluxo.ReadAsync(_bloco, cancellationToken).ConfigureAwait(false);
        BytesLidos += _disponivel;
        if (BytesLidos > _limiteDeBytes)
        {
            throw Recusa(MotivosDeRecusaDoArquivoSqlDne.ArquivoAcimaDoLimite);
        }

        return _disponivel > 0;
    }

    private bool AcumularAteAQuebra()
    {
        ReadOnlySpan<byte> restante = _bloco.AsSpan(_posicao, _disponivel - _posicao);
        int quebra = restante.IndexOf((byte)'\n');
        ReadOnlySpan<byte> trecho = quebra < 0 ? restante : restante[..quebra];
        if (_tamanhoDaLinha + trecho.Length > _linha.Length)
        {
            throw Recusa(MotivosDeRecusaDoArquivoSqlDne.LinhaAcimaDoLimite);
        }

        trecho.CopyTo(_linha.AsSpan(_tamanhoDaLinha));
        _tamanhoDaLinha += trecho.Length;
        _posicao += trecho.Length + (quebra < 0 ? 0 : 1);
        _terminouComQuebra = quebra >= 0;
        return _terminouComQuebra;
    }

    private string? TerminarNoFimDoArquivo()
    {
        if (_tamanhoDaLinha > 0)
        {
            return Concluir();
        }

        NumeroDaLinha--;
        return null;
    }

    private string Concluir()
    {
        ReadOnlySpan<byte> bytes = _linha.AsSpan(0, _tamanhoDaLinha);
        if (NumeroDaLinha == 1 && bytes.StartsWith(Encoding.UTF8.Preamble))
        {
            bytes = bytes[Encoding.UTF8.Preamble.Length..];
        }

        bool retorno = bytes.EndsWith("\r"u8);
        bytes = retorno ? bytes[..^1] : bytes;
        FimDeLinha = !_terminouComQuebra ? string.Empty : retorno ? "\r\n" : "\n";
        if (bytes.Length > LimitesDoPacoteDne.BytesPorLinha)
        {
            throw Recusa(MotivosDeRecusaDoArquivoSqlDne.LinhaAcimaDoLimite);
        }

        return Decodificar(bytes);
    }

    private string Decodificar(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return Utf8Estrito.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw Recusa(MotivosDeRecusaDoArquivoSqlDne.CodificacaoInvalida);
        }
    }
}
