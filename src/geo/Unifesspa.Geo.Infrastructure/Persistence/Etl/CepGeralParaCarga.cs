namespace Unifesspa.Geo.Infrastructure.Persistence.Etl;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Domain.ValueObjects;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Parsing;
using Unifesspa.Geo.Kernel.Results;

using static Unifesspa.Geo.Infrastructure.Persistence.Etl.GeoEtlUpsert;

/// <summary>
/// CEP geral de cidade ou distrito já validado. A cidade é obrigatória; o distrito só
/// vale se pertencer à mesma cidade, senão o CEP degrada para CEP geral da cidade.
/// </summary>
internal readonly record struct CepGeralParaCarga(
    string Cep,
    Guid CidadeId,
    Guid? DistritoId,
    string Uf,
    PosicaoGeografica Posicao,
    ProvenienciaDaCarga Proveniencia)
{
    public static CepGeralParaCarga? De(
        CepGeralCru linha,
        Dictionary<int, Guid> cidadesPorIdDne,
        Dictionary<int, LocalidadeResolvida> distritosPorIdDne,
        ProvenienciaDaCarga proveniencia,
        ContadorTabela contador)
    {
        string? cep = ChaveCodigo(linha.Cep);
        if (cep is null)
        {
            contador.ContarIgnoradoSemChave();
            return null;
        }
        if (ResolverCidade(cidadesPorIdDne, linha.CidadeIdDne) is not Guid cidadeId)
        {
            contador.ContarOrfao(cep);
            return null;
        }
        return Montar(linha, cep, cidadeId, distritosPorIdDne, proveniencia, contador);
    }

    private static CepGeralParaCarga Montar(
        CepGeralCru linha,
        string cep,
        Guid cidadeId,
        Dictionary<int, LocalidadeResolvida> distritosPorIdDne,
        ProvenienciaDaCarga proveniencia,
        ContadorTabela contador) =>
        new(
            cep,
            cidadeId,
            ResolverCoerente(distritosPorIdDne, linha.DistritoIdDne, cidadeId, contador),
            linha.Uf ?? string.Empty,
            CoordenadaDne.Ler(linha.Latitude, linha.Longitude),
            proveniencia);

    public Result<CepGeral> Criar() =>
        CepGeral.Importar(Cep, CidadeId, DistritoId, Uf, Posicao, Proveniencia);

    public Result Atualizar(CepGeral existente) =>
        existente.Atualizar(CidadeId, DistritoId, Uf, Posicao, Proveniencia);
}
