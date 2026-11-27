namespace Unifesspa.Geo.IntegrationTests.Etl;

using System.Data.Common;

using Microsoft.EntityFrameworkCore.Diagnostics;

/// <summary>Executa o gancho do teste no instante em que o EF Core vai confirmar a transação, antes do commit.</summary>
internal sealed class ObservadorDaConfirmacao : DbTransactionInterceptor
{
    private readonly Func<DbTransaction, Task> _aoConfirmar;

    public ObservadorDaConfirmacao(Func<DbTransaction, Task> aoConfirmar)
    {
        _aoConfirmar = aoConfirmar;
    }

    public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        await _aoConfirmar(transaction);
        return result;
    }
}
