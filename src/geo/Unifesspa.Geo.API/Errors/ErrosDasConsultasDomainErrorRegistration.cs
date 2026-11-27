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
            ErrosDasConsultas.ParametrosDeProximidadeAusentes,
            ErrosDasConsultas.LatitudeInvalida,
            ErrosDasConsultas.LongitudeInvalida,
            ErrosDasConsultas.RaioInvalido,
            ErrosDasConsultas.LimiteInvalido,
        }.Select(static definicao => new KeyValuePair<string, DomainErrorMapping>(definicao.Code, definicao));
}
