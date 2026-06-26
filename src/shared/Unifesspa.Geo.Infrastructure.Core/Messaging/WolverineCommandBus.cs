namespace Unifesspa.Geo.Infrastructure.Core.Messaging;

using Unifesspa.Geo.Application.Abstractions.Messaging;

// Inline-qualified Wolverine.IMessageBus para evitar colisão com Wolverine.ICommandBus
// (esta sim, do framework — não é a abstração canônica do projeto, ver ADR-0003).
internal sealed class WolverineCommandBus(Wolverine.IMessageBus bus) : ICommandBus
{
    public Task<TResponse> Send<TResponse>(
        ICommand<TResponse> command,
        CancellationToken ct = default)
        => bus.InvokeAsync<TResponse>(command, ct);
}
