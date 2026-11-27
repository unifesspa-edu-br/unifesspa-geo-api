namespace Unifesspa.Geo.Infrastructure.Core.Errors;

using Microsoft.AspNetCore.Http;

/// <summary>
/// As respostas de erro que a fronteira HTTP emite sem passar por um erro de domínio:
/// autenticação, negociação de versão de mídia, leitura da requisição, caminho sem operação,
/// método que o caminho não aceita, validação e falha inesperada. Cada emissor lê status, código e título daqui.
/// </summary>
public static class ErrosDaFronteiraHttp
{
    public static readonly DomainErrorMapping NaoAutenticado = new(
        StatusCodes.Status401Unauthorized, "uniplus.auth.unauthorized", "Não autenticado");

    public static readonly DomainErrorMapping AcessoNegado = new(
        StatusCodes.Status403Forbidden, "uniplus.auth.forbidden", "Acesso negado");

    public static readonly DomainErrorMapping VersaoDeMidiaNaoSuportada = new(
        StatusCodes.Status406NotAcceptable, "uniplus.contract.versao_nao_suportada", "Versão de mídia não suportada");

    public static readonly DomainErrorMapping ValorDaRequisicaoInvalido = new(
        StatusCodes.Status400BadRequest,
        "uniplus.requisicao.valor_invalido",
        "A requisição traz valor que não corresponde ao tipo declarado");

    public static readonly DomainErrorMapping CaminhoInexistente = new(
        StatusCodes.Status404NotFound,
        "uniplus.requisicao.caminho_inexistente",
        "A requisição pede um caminho que a API não oferece");

    public static readonly DomainErrorMapping MetodoNaoAceito = new(
        StatusCodes.Status405MethodNotAllowed,
        "uniplus.requisicao.metodo_nao_aceito",
        "A requisição usa um método que o caminho não aceita");

    public static readonly DomainErrorMapping ErroDeValidacao = new(
        StatusCodes.Status422UnprocessableEntity, "uniplus.validacao", "Erro de validação");

    public static readonly DomainErrorMapping ErroInesperado = new(
        StatusCodes.Status500InternalServerError, "uniplus.internal.unexpected", "Erro interno do servidor");
}
