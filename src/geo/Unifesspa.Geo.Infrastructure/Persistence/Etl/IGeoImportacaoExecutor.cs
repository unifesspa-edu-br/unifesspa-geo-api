namespace Unifesspa.Geo.Infrastructure.Persistence.Etl;

/// <summary>
/// Executa a carga pesada de uma execução já registrada. Porta interna
/// usada pelo <c>BackgroundService</c> — separada de <c>IGeoImportacaoService</c> (a
/// porta da API, que só dispara e acompanha), para que a borda não exponha a execução
/// longa.
/// </summary>
internal interface IGeoImportacaoExecutor
{
    /// <summary>
    /// Executa a carga reservada na transação única (importadores, marcação de não vigentes,
    /// conclusão, selo do cache e métricas), com a trava consultiva obtida no pedido. Não lança
    /// em falha de carga: marca a execução como <c>Falhou</c>, registra a métrica/log
    /// sanitizado e só então solta a trava.
    /// </summary>
    Task ExecutarAsync(CargaReservada carga, CancellationToken cancellationToken);

    /// <summary>
    /// Marca como <c>Falhou</c> uma execução enfileirada que não chegou a ser processada
    /// antes do desligamento do processo em segundo plano. Idempotente — só afeta a linha se ainda
    /// estiver <c>EmAndamento</c>, liberando o índice único parcial para novos disparos.
    /// </summary>
    Task MarcarInterrompidaNoDesligamentoAsync(Guid execucaoId, CancellationToken cancellationToken);

    /// <summary>
    /// Marca como <c>Falhou</c> uma execução cuja carga não pôde ser iniciada por erro inesperado
    /// do consumidor da fila. Idempotente — só afeta a linha se ainda estiver <c>EmAndamento</c>.
    /// </summary>
    Task MarcarFalhaInesperadaAsync(Guid execucaoId, CancellationToken cancellationToken);
}
