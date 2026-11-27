namespace Unifesspa.Geo.Domain.Errors;

using System.Globalization;

using Unifesspa.Geo.Kernel.Results;

/// <summary>Erros de domínio do envio do pacote, sempre com texto fixo (sem conteúdo do arquivo).</summary>
public static class ErrosDoPacoteDne
{
    private const long BytesPorKiB = 1024;
    private const long BytesPorMiB = BytesPorKiB * 1024;

    public static DomainError PacoteInvalido(string detalhe) =>
        new(GeoImportacaoErrorCodes.PacoteInvalido, detalhe);

    public static DomainError EmAndamento() =>
        new(GeoImportacaoErrorCodes.ImportacaoEmAndamento, "Já existe uma importação do Geo em andamento.");

    public static DomainError TipoDeConteudoNaoSuportado() =>
        new(GeoImportacaoErrorCodes.TipoDeConteudoNaoSuportado, "O envio deve ser um arquivo zip (application/zip).");

    public static DomainError PacoteAcimaDoLimite(long limiteEmBytes) =>
        new(
            GeoImportacaoErrorCodes.PacoteAcimaDoLimite,
            $"O pacote ultrapassa o tamanho máximo de {Formatar(limiteEmBytes)}.");

    private static string Formatar(long bytes)
    {
        if (bytes % BytesPorMiB == 0)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{bytes / BytesPorMiB} MiB");
        }

        return bytes % BytesPorKiB == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{bytes / BytesPorKiB} KiB")
            : string.Create(CultureInfo.InvariantCulture, $"{bytes} bytes");
    }
}
