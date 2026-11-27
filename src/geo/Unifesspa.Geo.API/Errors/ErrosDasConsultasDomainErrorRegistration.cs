namespace Unifesspa.Geo.API.Errors;

using Unifesspa.Geo.Infrastructure.Core.Errors;

internal sealed class ErrosDasConsultasDomainErrorRegistration : IDomainErrorRegistration
{
    public IEnumerable<KeyValuePair<string, DomainErrorMapping>> GetMappings() =>
        ErrosDasConsultas.Todas.Select(static definicao => new KeyValuePair<string, DomainErrorMapping>(definicao.Code, definicao));
}
