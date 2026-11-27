namespace Unifesspa.Geo.IntegrationTests.Admin;

using System.Diagnostics.CodeAnalysis;

using Unifesspa.Geo.API.Errors;
using Unifesspa.Geo.Infrastructure.Core.Errors;

/// <summary>Mapeador de erros com os registros reais do Geo, sem subir a aplicação.</summary>
internal sealed class MapeadorDeErrosParaTeste : IDomainErrorMapper
{
    private readonly Dictionary<string, DomainErrorMapping> _mapeamentos =
        new GeoDomainErrorRegistration().GetMappings().ToDictionary(m => m.Key, m => m.Value, StringComparer.Ordinal);

    public bool TryGetMapping(string code, [MaybeNullWhen(false)] out DomainErrorMapping mapping) =>
        _mapeamentos.TryGetValue(code, out mapping);
}
