namespace Unifesspa.Geo.IntegrationTests.Api;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Apoio das classes de teste da consulta de CEP: apaga o que existe e grava, numa única
/// gravação, os dados de São Paulo e de Marabá usados pelos cenários. Usado por composição.
/// </summary>
internal sealed class CenarioDaConsultaDoCep
{
    private readonly GeoPostgisFixture _fixture;

    public CenarioDaConsultaDoCep(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task PrepararAsync()
    {
        await GeoReferenceSeed.LimparAsync(_fixture);
        await using GeoDbContext ctx = _fixture.CreateDbContext();

        Pais brasil = GeoReferenceSeed.NovoBrasil();
        ctx.Paises.Add(brasil);
        AdicionarSaoPaulo(ctx, brasil);
        AdicionarMaraba(ctx, brasil);
        await ctx.SaveChangesAsync();
    }

    private static void AdicionarSaoPaulo(GeoDbContext ctx, Pais brasil)
    {
        Estado estado = GeoReferenceSeed.NovoEstado(brasil.Id, "SP", "São Paulo", regiao: "Sudeste", capital: "São Paulo");
        Cidade cidade = GeoReferenceSeed.NovaCidade(estado.Id, "SP", "3550308", "São Paulo");
        Bairro se = GeoReferenceSeed.NovoBairro(cidade.Id, "SP", "Sé");
        Bairro belaVista = GeoReferenceSeed.NovoBairro(cidade.Id, "SP", "Bela Vista");
        Bairro jardimPaulista = GeoReferenceSeed.NovoBairro(cidade.Id, "SP", "Jardim Paulista");
        ctx.Estados.Add(estado);
        ctx.Cidades.Add(cidade);
        ctx.Bairros.AddRange(se, belaVista, jardimPaulista);
        AdicionarFaixaEGrandeUsuario(ctx, cidade);
        AdicionarCepsComComplemento(ctx, cidade, se);
        AdicionarCepDeDoisLogradouros(ctx, cidade, belaVista, jardimPaulista);
        AdicionarCepsGeraisQueRepetemOutrasOrigens(ctx, cidade);
    }

    // CEP de grande usuário sem logradouro próprio: a cidade vem da faixa de São Paulo.
    private static void AdicionarFaixaEGrandeUsuario(GeoDbContext ctx, Cidade cidade)
    {
        ctx.CidadeFaixasCep.Add(GeoReferenceSeed.NovaCidadeFaixa(cidade.Id, "01000000", "01099999"));
        ctx.CepGrandesUsuarios.Add(GeoReferenceSeed.NovoGrandeUsuario("01051900", "Banco do Brasil Agência Centro"));
    }

    // CEP de logradouro único com um complemento; CEP com dois complementos, que deixa o
    // campo complemento nulo por ser ambíguo.
    private static void AdicionarCepsComComplemento(GeoDbContext ctx, Cidade cidade, Bairro se)
    {
        ctx.Logradouros.AddRange(
            GeoReferenceSeed.NovoLogradouro(
                cidade.Id, "SP", "01001000", "Praça da Sé", tipo: "Praça",
                bairroId: se.Id, latitude: -23.55m, longitude: -46.63m),
            GeoReferenceSeed.NovoLogradouro(
                cidade.Id, "SP", "01002000", "Rua das Flores", tipo: "Rua", bairroId: se.Id));
        ctx.LogradouroComplementos.AddRange(
            GeoReferenceSeed.NovoComplemento("01001000", "de 1 a 999 - lado ímpar"),
            GeoReferenceSeed.NovoComplemento("01002000", "lado par"),
            GeoReferenceSeed.NovoComplemento("01002000", "lado ímpar"));
    }

    // CEP compartilhado por dois logradouros (o desempate usa o nome normalizado) e um
    // logradouro não vigente, num CEP que não está em nenhuma faixa.
    private static void AdicionarCepDeDoisLogradouros(GeoDbContext ctx, Cidade cidade, Bairro belaVista, Bairro jardimPaulista)
    {
        ctx.Logradouros.AddRange(
            GeoReferenceSeed.NovoLogradouro(
                cidade.Id, "SP", "01310100", "Avenida Paulista", tipo: "Avenida", bairroId: belaVista.Id),
            GeoReferenceSeed.NovoLogradouro(
                cidade.Id, "SP", "01310100", "Alameda Santos", tipo: "Alameda", bairroId: jardimPaulista.Id),
            GeoReferenceSeed.NovoLogradouro(
                cidade.Id, "SP", "30000000", "Rua Obsoleta", vigente: false));
    }

    // CEP geral com o mesmo número de um logradouro (01001000) e de um grande usuário
    // (01051900): a consulta resolve primeiro por eles.
    private static void AdicionarCepsGeraisQueRepetemOutrasOrigens(GeoDbContext ctx, Cidade cidade)
    {
        ctx.CepGerais.AddRange(
            GeoReferenceSeed.NovoCepGeral("01001000", cidade.Id, "SP"),
            GeoReferenceSeed.NovoCepGeral("01051900", cidade.Id, "SP"));
    }

    private static void AdicionarMaraba(GeoDbContext ctx, Pais brasil)
    {
        Estado estado = GeoReferenceSeed.NovoEstado(brasil.Id, "PA", "Pará", regiao: "Norte", capital: "Belém");
        Cidade cidade = GeoReferenceSeed.NovaCidade(estado.Id, "PA", "1500402", "Marabá");
        Bairro cidadeNova = GeoReferenceSeed.NovoBairro(cidade.Id, "PA", "Cidade Nova");
        Distrito novaMaraba = GeoReferenceSeed.NovoDistrito(cidade.Id, "PA", "Nova Marabá");
        ctx.Estados.Add(estado);
        ctx.Cidades.Add(cidade);
        ctx.Bairros.Add(cidadeNova);
        ctx.Distritos.Add(novaMaraba);
        AdicionarFaixasDeMaraba(ctx, cidade, cidadeNova, novaMaraba);
        AdicionarCepsGeraisDeMaraba(ctx, cidade, novaMaraba);
    }

    // Bairro e distrito têm faixas disjuntas dentro de Marabá: 68507xxx é do bairro Cidade
    // Nova e 68508xxx é do distrito Nova Marabá, sem bairro.
    private static void AdicionarFaixasDeMaraba(GeoDbContext ctx, Cidade cidade, Bairro cidadeNova, Distrito novaMaraba)
    {
        ctx.CidadeFaixasCep.Add(GeoReferenceSeed.NovaCidadeFaixa(cidade.Id, "68500000", "68599999"));
        ctx.BairroFaixasCep.Add(GeoReferenceSeed.NovaBairroFaixa(cidadeNova.Id, "68507000", "68507999"));
        ctx.DistritoFaixasCep.Add(GeoReferenceSeed.NovaDistritoFaixa(novaMaraba.Id, "68508000", "68508999"));
    }

    // CEP geral oficial da cidade de Marabá e do distrito Nova Marabá. O 68508900 fica
    // dentro da faixa do distrito, o que prova a precedência sobre as faixas.
    private static void AdicionarCepsGeraisDeMaraba(GeoDbContext ctx, Cidade cidade, Distrito novaMaraba)
    {
        ctx.CepGerais.AddRange(
            GeoReferenceSeed.NovoCepGeral("68500000", cidade.Id, "PA"),
            GeoReferenceSeed.NovoCepGeral("68508900", cidade.Id, "PA", novaMaraba.Id));
    }
}
