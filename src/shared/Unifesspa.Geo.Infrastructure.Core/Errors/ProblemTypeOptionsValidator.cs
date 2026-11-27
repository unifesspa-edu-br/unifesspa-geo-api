namespace Unifesspa.Geo.Infrastructure.Core.Errors;

using Microsoft.Extensions.Options;

/// <summary>
/// Valida <see cref="ProblemTypeOptions"/> ao iniciar o serviço. O campo <c>type</c> não é
/// lido no caminho de sucesso; sem esta checagem, uma base errada só apareceria quando
/// alguém tentasse seguir o link de um erro.
/// </summary>
internal sealed class ProblemTypeOptionsValidator : IValidateOptions<ProblemTypeOptions>
{
    private const string Chave = $"{ProblemTypeOptions.SectionName}:{nameof(ProblemTypeOptions.BaseUri)}";
    private const string Exemplo = "Ex.: https://unifesspa-edu-br.github.io/uniplus-developers/erros/";
    private const string MotivoObrigatorio =
        "é obrigatório: sem ele o campo type do corpo de erro não aponta para a página do portal.";
    private const string MotivoNaoAbsoluta =
        "deve ser um endereço absoluto: quem consome a API não conhece o endereço do servidor.";
    private const string MotivoSemHttps = "deve usar HTTPS.";
    private const string MotivoComCredenciais =
        "não aceita usuário nem senha no endereço: o campo type das respostas de erro é público.";
    private const string MotivoComConsultaOuFragmento =
        "não aceita consulta nem fragmento: o código do erro é acrescentado ao fim do caminho.";

    public ValidateOptionsResult Validate(string? name, ProblemTypeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!string.IsNullOrEmpty(name) && !string.Equals(name, Options.DefaultName, StringComparison.Ordinal))
        {
            return ValidateOptionsResult.Skip;
        }

        string? falha = FalhaDaBase(options.BaseUri);

        return falha is null ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(falha);
    }

    private static string? FalhaDaBase(Uri? baseUri)
    {
        if (baseUri is null)
        {
            return $"{Chave} {MotivoObrigatorio} {Exemplo}";
        }

        if (!baseUri.IsAbsoluteUri)
        {
            return RecusaComOValorRecebido(MotivoNaoAbsoluta, baseUri);
        }

        if (!string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            return RecusaComOValorRecebido(MotivoSemHttps, baseUri);
        }

        if (baseUri.UserInfo.Length > 0)
        {
            return $"{Chave} {MotivoComCredenciais} {Exemplo}";
        }

        return baseUri.Query.Length > 0 || baseUri.Fragment.Length > 0
            ? RecusaComOValorRecebido(MotivoComConsultaOuFragmento, baseUri)
            : null;
    }

    private static string RecusaComOValorRecebido(string motivo, Uri baseUri) =>
        $"{Chave} {motivo} Recebido: '{baseUri.OriginalString}'. {Exemplo}";
}
