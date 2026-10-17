namespace Unifesspa.Geo.Application.Abstractions;

/// <summary>
/// Invalida o cache local de lookup de CEP da própria API do Geo após uma recarga do
/// ETL (Story #674, CA-05). A invalidação é por <strong>selo de versão</strong> (O(1)):
/// grava-se a versão vigente numa única chave; o lookup (F4, #676) compõe a chave de
/// cache com esse selo, de modo que entradas de versões antigas viram <em>miss</em> e
/// expiram por TTL — sem varredura (<c>SCAN</c>/<c>KEYS</c>) do keyspace compartilhado
/// e sem tocar cache de outro módulo. O consumo cross-módulo do Geo é por composição no
/// cliente; não há reader cross-módulo que mantenha cache derivado a invalidar.
/// </summary>
public interface IGeoCepCacheInvalidador
{
    /// <summary>
    /// Sela <paramref name="versaoVigente"/> como a versão corrente do lookup de CEP. O valor é
    /// um selo opaco, único por execução concluída (<c>{versao}.{execucaoId}</c>): a recarga da
    /// mesma versão também troca o selo. Não validar formato. Best-effort: uma falha de cache
    /// não deve abortar a carga (que já concluiu).
    /// </summary>
    Task InvalidarAsync(string versaoVigente, CancellationToken cancellationToken);
}
