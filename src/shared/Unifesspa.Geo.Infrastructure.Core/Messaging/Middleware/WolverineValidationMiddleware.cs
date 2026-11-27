namespace Unifesspa.Geo.Infrastructure.Core.Messaging.Middleware;

using FluentValidation;
using FluentValidation.Results;

using Microsoft.Extensions.DependencyInjection;

using Wolverine;
using Wolverine.Attributes;

/// <summary>
/// Middleware Wolverine que executa validação FluentValidation antes do handler
/// despachar a mensagem. Roda no pipeline tanto de commands quanto de queries
/// (registro filtrado por <see cref="MessagingMiddlewarePolicies"/>) e é o
/// ponto único de validação dos contratos CQRS (ADR-0003) — handlers nunca
/// invocam validators manualmente.
/// </summary>
/// <remarks>
/// Implementação não genérica de propósito: o middleware roda em cada chain
/// como código emitido pelo gerador do Wolverine, e ler a mensagem via
/// <see cref="Envelope.Message"/> + resolução dinâmica via
/// <see cref="IServiceProvider"/> evita declarar <c>object message</c> como
/// parâmetro (que conflita com a variável fortemente tipada da chain) e ao
/// mesmo tempo dispensa suporte a métodos genéricos no code-gen. Quando não
/// há validators registrados para o tipo concreto, retorna sem custo extra
/// (consultas a <see cref="IValidator{T}"/> via DI são O(1)).
/// </remarks>
public static class WolverineValidationMiddleware
{
    [WolverineBefore]
    public static async Task BeforeAsync(
        Envelope envelope,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(services);

        if (envelope.Message is null)
        {
            return;
        }

        IValidator[] validators = ValidadoresDaMensagem(envelope.Message, services);
        if (await FalhasAsync(validators, envelope.Message, cancellationToken).ConfigureAwait(false) is { Count: > 0 } failures)
        {
            throw new ValidationException(failures);
        }
    }

    private static IValidator[] ValidadoresDaMensagem(object mensagem, IServiceProvider services)
    {
        Type validatorType = typeof(IValidator<>).MakeGenericType(mensagem.GetType());

        return ((IEnumerable<object?>)services.GetServices(validatorType))
            .OfType<IValidator>()
            .ToArray();
    }

    private static async Task<List<ValidationFailure>> FalhasAsync(
        IValidator[] validators,
        object mensagem,
        CancellationToken cancellationToken)
    {
        if (validators.Length == 0)
        {
            return [];
        }

        // ValidationContext<object> aceita qualquer mensagem por instância, e o
        // IValidator não-genérico expõe ValidateAsync(IValidationContext, ct) —
        // suficiente porque a validação opera sobre a instância completa.
        ValidationContext<object> context = new(mensagem);
        ValidationResult[] results = await Task.WhenAll(
            validators.Select(v => v.ValidateAsync(context, cancellationToken)))
            .ConfigureAwait(false);

        return results.SelectMany(r => r.Errors).Where(f => f is not null).ToList();
    }
}
