namespace Unifesspa.Geo.Infrastructure.Core.Errors;

using Microsoft.Extensions.Options;

/// <summary>
/// Acrescenta o código do erro à base configurada em <see cref="ProblemTypeOptions"/>,
/// com exatamente uma barra entre os dois.
/// </summary>
public sealed class ProblemTypeUriFactory
{
    private readonly string _baseUri;

    public ProblemTypeUriFactory(IOptions<ProblemTypeOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Uri baseUri = options.Value.BaseUri
            ?? throw new InvalidOperationException("ProblemType:BaseUri é obrigatório.");
        _baseUri = baseUri.AbsoluteUri.TrimEnd('/') + "/";
    }

    public Uri Build(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return new Uri(_baseUri + code, UriKind.Absolute);
    }
}
