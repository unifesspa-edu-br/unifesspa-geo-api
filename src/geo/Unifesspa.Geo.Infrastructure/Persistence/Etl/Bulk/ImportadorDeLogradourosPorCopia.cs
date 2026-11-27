namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Bulk;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;

/// <summary>
/// Carga em lote (cópia binária) das tabelas de alto volume: os complementos por CEP
/// (~235 mil) e os ~1,4 milhão de logradouros (ADR-0092). Executa dentro da transação
/// ativa do <see cref="GeoDbContext"/>: cópia para tabela temporária sem restrições,
/// mesclagem por chave natural e remoção da temporária (<see cref="CargaDeTabelaPorCopia"/>).
/// </summary>
internal sealed class ImportadorDeLogradourosPorCopia
{
    private readonly GeoDbContext _contexto;
    private readonly ConfiguracaoDaCargaEmLote _configuracao;
    private readonly TimeProvider _clock;
    private readonly ILogger<ImportadorDeLogradourosPorCopia> _logger;

    public ImportadorDeLogradourosPorCopia(
        GeoDbContext contexto,
        IConfiguration configuration,
        TimeProvider clock,
        ILogger<ImportadorDeLogradourosPorCopia> logger)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _contexto = contexto;
        _configuracao = ConfiguracaoDaCargaEmLote.Ler(configuration);
        _clock = clock;
        _logger = logger;
    }

    public async Task ImportarAsync(
        IGeoFonteDados fonte,
        ResolucaoLocalidades resolucao,
        RelatorioImportacao relatorio,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fonte);
        ArgumentNullException.ThrowIfNull(resolucao);
        ArgumentNullException.ThrowIfNull(relatorio);

        ComplementosCarregadosPorCopia complementos = new(fonte);
        LogradourosCarregadosPorCopia logradouros = new(fonte, resolucao);
        SessaoDaCargaEmLote sessao = SessaoDaCargaEmLote.DaTransacaoAtiva(_contexto, _configuracao);

        // Um único instante para toda a carga: consistente e auditável.
        CargaDeTabelaPorCopia carga = new(sessao, _clock.GetUtcNow(), _logger);
        await carga.CarregarAsync(complementos, relatorio, cancellationToken).ConfigureAwait(false);
        await carga.CarregarAsync(logradouros, relatorio, cancellationToken).ConfigureAwait(false);
    }
}
