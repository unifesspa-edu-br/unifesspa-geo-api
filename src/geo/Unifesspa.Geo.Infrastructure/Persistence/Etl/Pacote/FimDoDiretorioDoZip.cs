namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

using System.Buffers.Binary;
using System.Runtime.InteropServices;

using Unifesspa.Geo.Domain.Errors;
using Unifesspa.Geo.Kernel.Results;

/// <summary>
/// Confere o registro de fim do diretório central do zip antes de abrir o arquivo com o leitor
/// de zip, que carrega todas as entradas na memória: o total de entradas (limite configurado), o tamanho do diretório e
/// a posição dele precisam caber nos limites do pacote. As mensagens são fixas, sem conteúdo do arquivo.
/// </summary>
internal static class FimDoDiretorioDoZip
{
    private const int TamanhoDoRegistro = 22;
    private const int ComentarioMaximo = ushort.MaxValue;
    private const ushort MarcadorDeZip64 = 0xFFFF;
    private const uint MarcadorDeZip64Grande = 0xFFFFFFFF;

    private static ReadOnlySpan<byte> Assinatura => [0x50, 0x4B, 0x05, 0x06];

    /// <summary>Devolve o erro de pacote inválido, ou <see langword="null"/> quando o fim do diretório está dentro dos limites.</summary>
    public static DomainError? Conferir(Stream zip, int quantidadeMaximaDeEntradas)
    {
        ArgumentNullException.ThrowIfNull(zip);

        byte[] cauda = LerCauda(zip, out long inicioDaCauda);
        int posicao = LocalizarUltimaAssinatura(cauda);
        return posicao < 0 || !ComentarioTerminaNoFimDoArquivo(cauda, posicao)
            ? ErrosDoPacoteDne.PacoteInvalido("O arquivo enviado não é um zip válido.")
            : ConferirRegistro(
                RegistroDeFim.Ler(cauda.AsSpan(posicao, TamanhoDoRegistro), inicioDaCauda + posicao),
                quantidadeMaximaDeEntradas);
    }

    private static byte[] LerCauda(Stream zip, out long inicioDaCauda)
    {
        int quantidade = (int)Math.Min(zip.Length, TamanhoDoRegistro + ComentarioMaximo);
        inicioDaCauda = zip.Length - quantidade;
        zip.Seek(inicioDaCauda, SeekOrigin.Begin);
        byte[] cauda = new byte[quantidade];
        zip.ReadExactly(cauda);
        return cauda;
    }

    // O leitor de zip usa a última assinatura do arquivo; a conferência tem de olhar o mesmo registro.
    private static int LocalizarUltimaAssinatura(byte[] cauda)
    {
        for (int posicao = cauda.Length - TamanhoDoRegistro; posicao >= 0; posicao--)
        {
            if (cauda.AsSpan(posicao, Assinatura.Length).SequenceEqual(Assinatura))
            {
                return posicao;
            }
        }

        return -1;
    }

    private static bool ComentarioTerminaNoFimDoArquivo(byte[] cauda, int posicao) =>
        BinaryPrimitives.ReadUInt16LittleEndian(cauda.AsSpan(posicao + 20)) == cauda.Length - posicao - TamanhoDoRegistro;

    private static DomainError? ConferirRegistro(RegistroDeFim registro, int quantidadeMaximaDeEntradas)
    {
        string? motivo = registro.MotivoDaRecusa(quantidadeMaximaDeEntradas);
        return motivo is null ? null : ErrosDoPacoteDne.PacoteInvalido(motivo);
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct RegistroDeFim(int Total, uint Tamanho, uint Deslocamento, long Posicao)
    {
        public bool UsaZip64 =>
            Total == MarcadorDeZip64 || Tamanho == MarcadorDeZip64Grande || Deslocamento == MarcadorDeZip64Grande;

        public string? MotivoDaRecusa(int quantidadeMaximaDeEntradas) => this switch
        {
            _ when UsaZip64 => "O pacote usa o formato zip64, que não é aceito.",
            _ when Total > quantidadeMaximaDeEntradas => $"O pacote tem mais de {quantidadeMaximaDeEntradas} entradas.",
            _ when Tamanho > LimitesDoPacoteDne.BytesNoDiretorioCentralDoZip => "O diretório do zip passa de 64 KiB.",
            _ when Deslocamento + (long)Tamanho != Posicao => "O diretório do zip é inconsistente com o tamanho do arquivo.",
            _ => null,
        };

        public static RegistroDeFim Ler(ReadOnlySpan<byte> registro, long posicao) =>
            new(
                Math.Max(
                    BinaryPrimitives.ReadUInt16LittleEndian(registro[8..]),
                    BinaryPrimitives.ReadUInt16LittleEndian(registro[10..])),
                BinaryPrimitives.ReadUInt32LittleEndian(registro[12..]),
                BinaryPrimitives.ReadUInt32LittleEndian(registro[16..]),
                posicao);
    }
}
