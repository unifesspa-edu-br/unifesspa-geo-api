namespace Unifesspa.Geo.IntegrationTests.Formato;

using Microsoft.AspNetCore.Mvc;

/// <summary>
/// O que a negociação da versão de mídia produziu: a recusa 406 ou, quando aceita, o tipo de
/// conteúdo e os valores de <c>Vary</c> da resposta.
/// </summary>
internal sealed record DesfechoDaNegociacao(ObjectResult? Recusa, string? TipoDeConteudo, IReadOnlyList<string> Vary)
{
    public ProblemDetails Problema => (ProblemDetails)Recusa!.Value!;
}
