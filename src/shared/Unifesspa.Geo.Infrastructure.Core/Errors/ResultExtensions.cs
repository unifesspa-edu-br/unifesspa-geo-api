namespace Unifesspa.Geo.Infrastructure.Core.Errors;

using System.Diagnostics.CodeAnalysis;

using Kernel.Results;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "Extension method class precisa ser public para ser acessível nos projetos API que referenciam Infrastructure.Core.")]
public static class ResultExtensions
{
    public static IActionResult ToActionResult<T>(this Result<T> result, IDomainErrorMapper mapper)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(mapper);
        if (result.IsSuccess)
            throw new InvalidOperationException("ToActionResult deve ser chamado apenas em caso de falha.");
        return BuildProblemDetailsResult(result.Error!, mapper);
    }

    public static IActionResult ToActionResult(this Result result, IDomainErrorMapper mapper)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(mapper);
        if (result.IsSuccess)
            throw new InvalidOperationException("ToActionResult deve ser chamado apenas em caso de falha.");
        return BuildProblemDetailsResult(result.Error!, mapper);
    }

    private static ObjectResult BuildProblemDetailsResult(DomainError error, IDomainErrorMapper mapper)
    {
        DomainErrorMapping mapping = MapeamentoOuFalha(error, mapper);

        // Invariante: error.Message não deve conter PII (CPF, e-mail, nome).
        // O linter AssertNoPiiAsync detecta violações nos testes de integração.
        ProblemDetails problem = mapping.ComoProblema(mapper.GetProblemTypeUri(mapping.Code), error.Message);

        return new ObjectResult(problem) { StatusCode = mapping.Status };
    }

    private static DomainErrorMapping MapeamentoOuFalha(DomainError error, IDomainErrorMapper mapper)
    {
        if (mapper.TryGetMapping(error.Code, out DomainErrorMapping? mapping))
        {
            return mapping;
        }

        throw new InvalidOperationException(
            $"O erro de domínio '{error.Code}' não tem mapeamento HTTP registrado; todo erro devolvido por uma ação precisa estar em um IDomainErrorRegistration.");
    }
}
