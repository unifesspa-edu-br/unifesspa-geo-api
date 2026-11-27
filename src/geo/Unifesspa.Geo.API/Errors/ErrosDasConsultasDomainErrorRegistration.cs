namespace Unifesspa.Geo.API.Errors;

using Unifesspa.Geo.Infrastructure.Core.Errors;

internal sealed class ErrosDasConsultasDomainErrorRegistration : IDomainErrorRegistration
{
    public IEnumerable<KeyValuePair<string, DomainErrorMapping>> GetMappings() =>
        new[]
        {
            ErrosDasConsultas.CepFormatoInvalido,
            ErrosDasConsultas.CepNaoEncontrado,
            ErrosDasConsultas.CodigoIbgeInvalido,
            ErrosDasConsultas.CidadeNaoEncontrada,
            ErrosDasConsultas.UfInvalida,
            ErrosDasConsultas.EstadoNaoEncontrado,
            ErrosDasConsultas.TermoDeBuscaMuitoLongo,
        }.Select(static definicao => new KeyValuePair<string, DomainErrorMapping>(definicao.Code, definicao));
}
