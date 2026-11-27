namespace Unifesspa.Geo.Infrastructure.Core.Errors;

using Kernel.Results;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Resposta para o valor de rota ou de consulta que não vira o tipo declarado (por exemplo,
/// <c>lat=abc</c> num parâmetro numérico). Substitui a resposta padrão do framework, que não traz
/// <c>code</c> nem <c>traceId</c>, aponta o <c>type</c> para a RFC do status e descreve o erro em inglês.
/// </summary>
/// <remarks>
/// O Geo não lê corpo pelo framework nem valida modelo: toda recusa do model binding que chega aqui
/// é valor que não converte. A mensagem do framework nunca é repassada, porque ela repete o valor
/// enviado; o detalhe cita só o nome do parâmetro.
/// </remarks>
internal static class RecusaDaLeituraDaRequisicao
{
    private const string Sufixo = "com valor que não corresponde ao tipo declarado.";

    public static IActionResult Responder(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        IDomainErrorMapper mapper = context.HttpContext.RequestServices.GetRequiredService<IDomainErrorMapper>();
        DomainError erro = new(ErrosDaFronteiraHttp.ValorDaRequisicaoInvalido.Code, Detalhe(NomesReprovados(context.ModelState)));

        return Result.Failure(erro).ToActionResult(mapper);
    }

    private static string[] NomesReprovados(ModelStateDictionary modelState) =>
        [.. modelState
            .Where(static entrada => entrada.Value?.ValidationState == ModelValidationState.Invalid && entrada.Key.Length > 0)
            .Select(static entrada => entrada.Key)
            .Order(StringComparer.Ordinal)];

    private static string Detalhe(string[] nomes) => nomes.Length switch
    {
        0 => "A requisição traz valor que não corresponde ao tipo declarado.",
        1 => $"A requisição traz o parâmetro '{nomes[0]}' {Sufixo}",
        _ => $"A requisição traz os parâmetros {string.Join(", ", nomes.Select(static nome => $"'{nome}'"))} {Sufixo}",
    };
}
