namespace Unifesspa.Geo.IntegrationTests.Etl;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using Unifesspa.Geo.Domain.Entities;

/// <summary>Faz falhar, como o banco indisponível faria, a gravação que marca uma execução como Falhou.</summary>
internal sealed class FalhaAoGravarOFimDaCarga : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        bool gravaFalhou = eventData.Context!.ChangeTracker
            .Entries<GeoImportacaoExecucao>()
            .Any(e => e.State == EntityState.Modified && e.Entity.Status == StatusImportacao.Falhou);

        return gravaFalhou
            ? throw new DbUpdateException("Falha simulada ao gravar o fim da carga.")
            : ValueTask.FromResult(result);
    }
}
