namespace Unifesspa.Geo.IntegrationTests;

using NetTopologySuite.Geometries;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Domain.ValueObjects;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>Cidade, indicador e faixa de CEP completos, para provar a gravação e a releitura.</summary>
internal sealed record CenarioDaCidadeDeMaraba(Cidade Maraba, CidadeIndicador Indicador, CidadeFaixaCep Faixa)
{
    private const string VersaoDataset = "202601";

    public static CenarioDaCidadeDeMaraba Montar(Guid estadoId)
    {
        Cidade maraba = NovaCidade(estadoId);
        return new CenarioDaCidadeDeMaraba(maraba, NovoIndicador(maraba), NovaFaixa(maraba));
    }

    public object[] Entidades() => [Maraba, Indicador, Faixa];

    private static Cidade NovaCidade(Guid estadoId) =>
        GeoTestKeys.Ok(Cidade.Importar(
            codigoIbge: GeoTestKeys.CodigoIbge(),
            estado: new EstadoDaCidade(estadoId, "PA"),
            nome: new NomeComFormaDeBusca("Marabá", "maraba"),
            ddd: "94",
            posicao: new PosicaoGeografica(-5.36867m, -49.11731m, new Point(-49.11731, -5.36867) { SRID = 4326 }),
            divisao: new DivisaoRegionalDaCidade(
                new RegiaoGeografica("1503", "Sudeste Paraense"),
                new RegiaoGeografica("15009", "Marabá"),
                new RegiaoGeografica("1503", "Marabá"),
                new RegiaoGeografica("150009", "Marabá")),
            proveniencia: new ProvenienciaDaCarga(VersaoDataset)));

    private static CidadeIndicador NovoIndicador(Cidade maraba) =>
        GeoTestKeys.Ok(CidadeIndicador.Importar(
            cidadeId: maraba.Id, gentilico: "marabaense", prefeito: null,
            areaKm2: 15128.058m, populacaoResidente: 233669, densidadeDemografica: 15.45m,
            escolarizacao6a14: 96.8m, idh: 0.668m, mortalidadeInfantil: 17.2m,
            receitas: 1_200_000_000.00m, despesas: 1_100_000_000.00m, pibPerCapita: 28000.50m,
            aniversario: "05/04", versaoDataset: VersaoDataset));

    private static CidadeFaixaCep NovaFaixa(Cidade maraba) =>
        GeoTestKeys.Ok(CidadeFaixaCep.Importar(
            cidadeId: maraba.Id, faixa: new FaixaDeCep("68500000", "68519999"),
            proveniencia: new ProvenienciaDaCarga(VersaoDataset)));
}
