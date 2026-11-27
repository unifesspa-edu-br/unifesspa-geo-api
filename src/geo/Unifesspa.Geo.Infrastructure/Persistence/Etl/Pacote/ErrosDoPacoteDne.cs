namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

using System.Globalization;

using Unifesspa.Geo.Domain.Errors;
using Unifesspa.Geo.Kernel.Results;

/// <summary>Erros de domínio do recebimento do pacote, sempre com texto fixo (sem conteúdo do arquivo).</summary>
internal static class ErrosDoPacoteDne
{
    public static DomainError PacoteInvalido(string detalhe) =>
        new(GeoImportacaoErrorCodes.PacoteInvalido, detalhe);

    public static DomainError PacoteAcimaDoLimite(long limiteEmBytes) =>
        new(
            GeoImportacaoErrorCodes.PacoteAcimaDoLimite,
            string.Create(CultureInfo.InvariantCulture, $"O pacote ultrapassa o tamanho máximo de {limiteEmBytes} bytes."));
}
