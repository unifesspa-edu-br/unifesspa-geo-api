namespace Unifesspa.Geo.Infrastructure.Persistence.Etl;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;
using Unifesspa.Geo.Kernel.Results;

using static Unifesspa.Geo.Infrastructure.Persistence.Etl.GeoEtlUpsert;

/// <summary>Faixa de CEP de distrito ou bairro com a localidade resolvida, pronta para ser criada ou atualizada.</summary>
internal readonly record struct FaixaParaCarga(
    Guid LocalidadeId,
    string CepInicial,
    string CepFinal,
    string Versao)
{
    public string Chave => ChaveFaixa(LocalidadeId, CepInicial, CepFinal);

    public static FaixaParaCarga? De(
        FaixaLocalidadeCru linha,
        Dictionary<int, LocalidadeResolvida> localidadesPorIdDne,
        string versao,
        ContadorTabela contador)
    {
        if (linha.IdPaiDne is not int idPai || !localidadesPorIdDne.TryGetValue(idPai, out LocalidadeResolvida localidade))
        {
            contador.ContarOrfao(IdDaFonteParaRelatorio(linha.IdPaiDne));
            return null;
        }
        string? cepInicial = ChaveCodigo(linha.FaixaIni);
        string? cepFinal = ChaveCodigo(linha.FaixaFim);
        if (cepInicial is null || cepFinal is null)
        {
            contador.ContarIgnoradoSemChave();
            return null;
        }
        return new FaixaParaCarga(localidade.Id, cepInicial, cepFinal, versao);
    }

    public Result<DistritoFaixaCep> CriarFaixaDeDistrito() =>
        DistritoFaixaCep.Importar(LocalidadeId, CepInicial, CepFinal, Versao);

    public Result Atualizar(DistritoFaixaCep faixa) => faixa.Atualizar(Versao);

    public Result<BairroFaixaCep> CriarFaixaDeBairro() =>
        BairroFaixaCep.Importar(LocalidadeId, CepInicial, CepFinal, Versao);

    public Result Atualizar(BairroFaixaCep faixa) => faixa.Atualizar(Versao);
}
