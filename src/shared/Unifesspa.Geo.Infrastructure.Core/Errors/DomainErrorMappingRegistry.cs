namespace Unifesspa.Geo.Infrastructure.Core.Errors;

using System.Diagnostics.CodeAnalysis;

internal sealed class DomainErrorMappingRegistry : IDomainErrorMapper
{
    private readonly Dictionary<string, DomainErrorMapping> _mappings;
    private readonly ProblemTypeUriFactory _problemTypeUriFactory;

    public DomainErrorMappingRegistry(
        IEnumerable<IDomainErrorRegistration> registrations,
        ProblemTypeUriFactory problemTypeUriFactory)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(problemTypeUriFactory);
        var dict = new Dictionary<string, DomainErrorMapping>(StringComparer.OrdinalIgnoreCase);
        foreach (IDomainErrorRegistration registration in registrations)
        {
            foreach (KeyValuePair<string, DomainErrorMapping> pair in registration.GetMappings())
            {
                dict[pair.Key] = pair.Value;
            }
        }
        _mappings = dict;
        _problemTypeUriFactory = problemTypeUriFactory;
    }

    public bool TryGetMapping(string code, [MaybeNullWhen(false)] out DomainErrorMapping mapping)
        => _mappings.TryGetValue(code, out mapping);

    public Uri GetProblemTypeUri(string code) => _problemTypeUriFactory.Build(code);
}
