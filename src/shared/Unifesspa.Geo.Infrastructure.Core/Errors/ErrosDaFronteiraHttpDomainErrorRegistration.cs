namespace Unifesspa.Geo.Infrastructure.Core.Errors;

internal sealed class ErrosDaFronteiraHttpDomainErrorRegistration : IDomainErrorRegistration
{
    public IEnumerable<KeyValuePair<string, DomainErrorMapping>> GetMappings() =>
        new[]
        {
            ErrosDaFronteiraHttp.NaoAutenticado,
            ErrosDaFronteiraHttp.AcessoNegado,
            ErrosDaFronteiraHttp.VersaoDeMidiaNaoSuportada,
            ErrosDaFronteiraHttp.ValorDaRequisicaoInvalido,
            ErrosDaFronteiraHttp.CaminhoInexistente,
            ErrosDaFronteiraHttp.MetodoNaoAceito,
            ErrosDaFronteiraHttp.ErroDeValidacao,
            ErrosDaFronteiraHttp.ErroInesperado,
        }.Select(static definicao => new KeyValuePair<string, DomainErrorMapping>(definicao.Code, definicao));
}
