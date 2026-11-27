namespace Unifesspa.Geo.Infrastructure.Persistence.Etl;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Domain.ValueObjects;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;
using Unifesspa.Geo.Kernel.Results;

using static Unifesspa.Geo.Infrastructure.Persistence.Etl.RegrasDaCargaPorChaveNatural;

/// <summary>Faixa de CEP de distrito ou bairro com a localidade resolvida, pronta para ser criada ou atualizada.</summary>
internal readonly record struct FaixaParaCarga(
    Guid LocalidadeId,
    FaixaDeCep Faixa,
    ProvenienciaDaCarga Proveniencia)
{
    public string Chave => ChaveFaixa(LocalidadeId, Faixa.CepInicial!, Faixa.CepFinal!);

    public static FaixaParaCarga? De(
        FaixaLocalidadeCru linha,
        Dictionary<int, LocalidadeResolvida> localidadesPorIdDne,
        ProvenienciaDaCarga proveniencia,
        ContadorTabela contador)
    {
        if (linha.IdPaiDne is not int idPai || !localidadesPorIdDne.TryGetValue(idPai, out LocalidadeResolvida localidade))
        {
            contador.ContarOrfao(IdDaFonteParaRelatorio(linha.IdPaiDne));
            return null;
        }
        FaixaDeCep faixa = new(linha.FaixaIni, linha.FaixaFim);
        if (!faixa.TemCepInicial || !faixa.TemCepFinal)
        {
            contador.ContarIgnoradoSemChave();
            return null;
        }
        return new FaixaParaCarga(localidade.Id, faixa, proveniencia);
    }

    public Result<DistritoFaixaCep> CriarFaixaDeDistrito() =>
        DistritoFaixaCep.Importar(LocalidadeId, Faixa, Proveniencia);

    public Result Atualizar(DistritoFaixaCep existente) => existente.Atualizar(Proveniencia);

    public Result<BairroFaixaCep> CriarFaixaDeBairro() =>
        BairroFaixaCep.Importar(LocalidadeId, Faixa, Proveniencia);

    public Result Atualizar(BairroFaixaCep existente) => existente.Atualizar(Proveniencia);
}
