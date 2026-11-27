namespace Unifesspa.Geo.Infrastructure.Persistence.Etl;

/// <summary>
/// Identificador que marca no cache de CEP a execução cuja versão está publicada.
/// </summary>
internal static class SeloDoCacheDeCep
{
    public static string Identificar(string versao, Guid execucaoId) => $"{versao}.{execucaoId:N}";
}
