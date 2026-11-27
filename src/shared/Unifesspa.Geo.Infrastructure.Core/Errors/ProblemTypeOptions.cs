namespace Unifesspa.Geo.Infrastructure.Core.Errors;

/// <summary>
/// Base do campo <c>type</c> do corpo de erro (RFC 9457): o endereço sob o qual o portal
/// de desenvolvedores publica a página de cada código de erro.
/// </summary>
/// <remarks>
/// Sem valor padrão no código: cada ambiente declara o seu, e configuração ausente impede
/// o início do serviço em vez de devolver um <c>type</c> que não leva a página nenhuma.
/// </remarks>
public sealed class ProblemTypeOptions
{
    /// <summary>Seção de configuração (<c>ProblemType</c> no appsettings e <c>ProblemType__*</c> na variável de ambiente).</summary>
    public const string SectionName = "ProblemType";

    /// <summary>
    /// Endereço absoluto HTTPS do portal, ao qual o código do erro é acrescentado.
    /// A barra final é opcional: a fábrica a normaliza.
    /// </summary>
    public Uri? BaseUri { get; set; }
}
