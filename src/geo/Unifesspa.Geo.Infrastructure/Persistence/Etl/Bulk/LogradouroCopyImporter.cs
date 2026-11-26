namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Bulk;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;

/// <summary>
/// Carga em lote (cópia binária) das tabelas de alto volume: os complementos por CEP
/// (~235 mil) e os ~1,4 milhão de logradouros (ADR-0092). Os dois modos (<see cref="ModoCarga"/>)
/// usam a mesma estratégia, em uma única conexão: cópia para uma tabela temporária sem
/// restrições, mesclagem no destino e remoção da temporária (<see cref="CargaDeTabelaPorCopia"/>).
/// No modo <see cref="ModoCarga.Inicial"/> o logradouro ainda é esvaziado e perde os índices
/// pesados durante a cópia (<see cref="CargaInicialDoLogradouro"/>); na
/// <see cref="ModoCarga.Recarga"/> os índices permanecem.
/// </summary>
internal sealed class LogradouroCopyImporter
{
    private readonly ConfiguracaoDaCargaEmLote _configuracao;
    private readonly TimeProvider _clock;
    private readonly ILogger<LogradouroCopyImporter> _logger;

    public LogradouroCopyImporter(IConfiguration configuration, TimeProvider clock, ILogger<LogradouroCopyImporter> logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _configuracao = ConfiguracaoDaCargaEmLote.Ler(configuration);
        _clock = clock;
        _logger = logger;
    }

    public async Task ImportarAsync(
        IGeoFonteDados fonte, ModoCarga modo, ResolucaoLocalidades resolucao, RelatorioImportacao relatorio, CancellationToken cancellationToken)
    {
        ComplementosCarregadosPorCopia complementos = new(fonte);
        LogradourosCarregadosPorCopia logradouros = new(fonte, resolucao);
        SessaoDaCargaEmLote sessao = await SessaoDaCargaEmLote.AbrirAsync(_configuracao, cancellationToken).ConfigureAwait(false);
        await using (sessao.ConfigureAwait(false))
        {
            // Um único instante para toda a carga: consistente e auditável.
            CargaDeTabelaPorCopia carga = new(sessao, _clock.GetUtcNow(), _logger);
            await carga.CarregarAsync(complementos, relatorio, cancellationToken).ConfigureAwait(false);
            await CarregarLogradourosAsync(sessao, modo, () => carga.CarregarAsync(logradouros, relatorio, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
    }

    private Task CarregarLogradourosAsync(SessaoDaCargaEmLote sessao, ModoCarga modo, Func<Task> carga, CancellationToken cancellationToken) =>
        modo == ModoCarga.Inicial
            ? new CargaInicialDoLogradouro(sessao, _logger).ExecutarAsync(carga, cancellationToken)
            : carga();
}
