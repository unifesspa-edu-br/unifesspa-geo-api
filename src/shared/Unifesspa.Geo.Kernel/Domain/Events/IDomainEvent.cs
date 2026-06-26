namespace Unifesspa.Geo.Kernel.Domain.Events;

public interface IDomainEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredOn { get; }
}
