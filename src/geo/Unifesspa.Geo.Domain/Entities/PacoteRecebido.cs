namespace Unifesspa.Geo.Domain.Entities;

using System.Linq;

/// <summary>
/// Pacote dos Correios recebido para uma carga: a versão (<c>AAAAMM</c>) lida do conteúdo,
/// o resumo SHA-256 do arquivo (64 hexadecimais minúsculos) e o tamanho em bytes.
/// </summary>
public sealed record PacoteRecebido(string Versao, string Sha256, long TamanhoEmBytes)
{
    private const int TamanhoDoResumoSha256 = 64;

    /// <summary>Indica que o resumo SHA-256 está no formato esperado e que o tamanho é positivo.</summary>
    public bool EhConsistente =>
        TamanhoEmBytes > 0
        && Sha256 is { Length: TamanhoDoResumoSha256 }
        && Sha256.All(char.IsAsciiHexDigitLower);
}
