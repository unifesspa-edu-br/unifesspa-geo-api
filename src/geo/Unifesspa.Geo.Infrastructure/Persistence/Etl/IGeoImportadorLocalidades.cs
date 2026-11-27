namespace Unifesspa.Geo.Infrastructure.Persistence.Etl;

using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;

/// <summary>
/// Importador das folhas da hierarquia do Geo (Distrito/Bairro + faixas, CEP de grande
/// usuário, complementos e logradouros) a partir do dataset DNE (ADR-0092).
/// Complementa o <see cref="IGeoImportador"/> (topo País/Estado/Cidade): o topo
/// precisa estar carregado antes na mesma transação, pois as folhas resolvem
/// as FKs <c>cidade_id</c>/<c>distrito_id</c>/<c>bairro_id</c> (int4 da fonte) para Guid.
/// </summary>
internal interface IGeoImportadorLocalidades
{
    /// <summary>
    /// Importa as folhas a partir da <paramref name="fonte"/> dentro da transação ativa.
    /// Órfãos (FK <c>cidade_id</c> ausente) e dados sujos são tolerados (vão
    /// ao relatório, não abortam); falha de infraestrutura aborta a carga.
    /// </summary>
    Task<RelatorioImportacao> ImportarAsync(IGeoFonteDados fonte, CancellationToken cancellationToken);
}
