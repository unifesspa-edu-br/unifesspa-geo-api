namespace Unifesspa.Geo.Infrastructure.Core.Persistence.Interceptors;

using Kernel.Domain.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

// Auditoria automática de criação e modificação (ADR-0033): para toda EntityBase,
// preenche CreatedAt em Added e UpdatedAt em Modified. Os dois campos têm setter
// protegido, então este interceptor é a única origem desses instantes.
//
// Registrado como Scoped para acompanhar o ciclo de vida do DbContext.
public sealed class AuditableInterceptor : SaveChangesInterceptor
{
    private readonly TimeProvider _timeProvider;

    // TimeProvider é obrigatório (sem fallback TimeProvider.System): o relógio
    // é sempre injetado pela DI (Singleton).
    public AuditableInterceptor(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData?.Context is not null)
        {
            ApplyAuditFields(eventData.Context);
        }

        return base.SavingChangesAsync(eventData!, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData?.Context is not null)
        {
            ApplyAuditFields(eventData.Context);
        }

        return base.SavingChanges(eventData!, result);
    }

    private void ApplyAuditFields(DbContext context)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();

        foreach (Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<EntityBase> entry
            in context.ChangeTracker.Entries<EntityBase>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Property(nameof(EntityBase.CreatedAt)).CurrentValue = now;
                    break;

                case EntityState.Modified:
                    entry.Property(nameof(EntityBase.UpdatedAt)).CurrentValue = now;
                    break;
            }
        }
    }
}
