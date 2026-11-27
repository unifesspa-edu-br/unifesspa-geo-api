namespace Unifesspa.Geo.IntegrationTests.Etl;

using System.Data.Common;

using Microsoft.EntityFrameworkCore.Diagnostics;

/// <summary>A confirmação chega ao banco e vale, mas a resposta falha: o resultado é incerto para a carga.</summary>
internal sealed class ConfirmacaoEfetivadaComFalhaNaResposta : DbTransactionInterceptor
{
    public override Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default) =>
        Task.FromException(new InvalidOperationException("Falha simulada depois da confirmação efetivada."));
}

/// <summary>A confirmação não chega a valer: o banco desfaz a transação e a carga recebe a falha.</summary>
internal sealed class ConfirmacaoDesfeitaPeloBanco : DbTransactionInterceptor
{
    public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        await transaction.RollbackAsync(CancellationToken.None);
        throw new InvalidOperationException("Falha simulada: o banco desfez a transação na confirmação.");
    }
}
