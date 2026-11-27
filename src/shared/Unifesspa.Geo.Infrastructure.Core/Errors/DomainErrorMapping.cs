namespace Unifesspa.Geo.Infrastructure.Core.Errors;

using System.Diagnostics;

using Microsoft.AspNetCore.Mvc;

public sealed record DomainErrorMapping(int Status, string Code, string Title)
{
    /// <summary>
    /// Monta o corpo <c>application/problem+json</c> (RFC 9457) desta definição de erro. As extensões
    /// saem na ordem <c>code</c>, <paramref name="extensoesDaRecusa"/> e <c>traceId</c>: a ordem das
    /// chaves faz parte do corpo publicado.
    /// </summary>
    /// <param name="tipo">Endereço da página do erro no portal (campo <c>type</c>).</param>
    /// <param name="detalhe">Descrição desta ocorrência; nunca pode conter dado pessoal.</param>
    /// <param name="extensoesDaRecusa">Extensões próprias da recusa, entre o código e o rastreio.</param>
    internal ProblemDetails ComoProblema(
        Uri tipo,
        string detalhe,
        IEnumerable<KeyValuePair<string, object?>>? extensoesDaRecusa = null)
    {
        ProblemDetails problema = new()
        {
            Status = Status,
            Type = tipo.AbsoluteUri,
            Title = Title,
            Detail = detalhe,
            Instance = $"urn:uuid:{Guid.CreateVersion7()}",
        };

        foreach (KeyValuePair<string, object?> extensao in ExtensoesNaOrdemPublicada(extensoesDaRecusa))
            problema.Extensions[extensao.Key] = extensao.Value;
        return problema;
    }

    private IEnumerable<KeyValuePair<string, object?>> ExtensoesNaOrdemPublicada(
        IEnumerable<KeyValuePair<string, object?>>? extensoesDaRecusa)
    {
        yield return new("code", Code);

        foreach (KeyValuePair<string, object?> extensao in extensoesDaRecusa ?? [])
            yield return extensao;

        yield return new("traceId", Activity.Current?.TraceId.ToHexString() ?? Guid.CreateVersion7().ToString("N"));
    }
}
