namespace Unifesspa.Geo.IntegrationTests;

using AwesomeAssertions;

using Infrastructure;

using Microsoft.EntityFrameworkCore;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Domain.ValueObjects;
using Unifesspa.Geo.Infrastructure.Persistence;

/// <summary>
/// Persistência de Logradouro + LogradouroComplemento + CepGrandeUsuario contra
/// Postgres+PostGIS real (story #671). Foco: cep indexado NÃO único (chave de upsert
/// composta), complemento por CEP sem FK a logradouro, grande usuário sem cidade,
/// Ativo bool. Fixture compartilhada — chaves únicas por teste (<see cref="GeoTestKeys"/>).
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class LogradouroPersistenceTests
{
    private const string VersaoDataset = "202601";

    private readonly GeoPostgisFixture _fixture;
    private readonly GravacaoNoBanco _banco;

    public LogradouroPersistenceTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _banco = new GravacaoNoBanco(fixture);
    }

    [Fact(DisplayName = "CA-01: Logradouro (cidade/bairro) + Complemento + GrandeUsuario persistem com Point 4326 e Ativo")]
    public async Task Logradouro_PersisteELe()
    {
        CidadeSemeada seed = CidadeSemeada.Nova();
        CenarioDoLogradouroDaSe cenario = CenarioDoLogradouroDaSe.Montar(seed.Cidade.Id);

        await _banco.GravarAsync(seed.EntidadesCom(cenario.Entidades()));

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        Logradouro lido = await leitura.Logradouros.SingleAsync(l => l.Id == cenario.Logradouro.Id);

        ConferirLogradouro(lido, seed.Cidade.Id, cenario.Bairro.Id);
        (await leitura.CepGrandesUsuarios.SingleAsync(g => g.Id == cenario.GrandeUsuario.Id)).Nome.Should().Be("UNESP");
    }

    [Fact(DisplayName = "CA-02: cep compartilhado por vários logradouros coexiste; (cep+nome+cidade) é a chave de upsert")]
    public async Task Logradouro_CepCompartilhado_Upsert()
    {
        CidadeSemeada seed = CidadeSemeada.Nova();
        string cepGeral = GeoTestKeys.Cep();

        // Dois logradouros distintos com o MESMO cep geral — coexistem (cep não é único).
        Logradouro l1 = NovoLogradouro(cepGeral, "Rua A", "rua a", seed.Cidade.Id);
        Logradouro l2 = NovoLogradouro(cepGeral, "Rua B", "rua b", seed.Cidade.Id);
        // Mesma chave de upsert que l1 (cep+nome+cidade) — deve ser barrada.
        Logradouro l1Dup = NovoLogradouro(cepGeral, "Rua A", "rua a", seed.Cidade.Id);

        await _banco.GravarAsync(seed.EntidadesCom(l1, l2));
        await ExigirDoisLogradourosNoCepAsync(cepGeral);
        await _banco.ExigirViolacaoUniqueAsync(l1Dup, "ix_logradouro_natural");
    }

    [Fact(DisplayName = "CA-03: complemento é por (cep, complemento_normalizado), sem FK a logradouro")]
    public async Task LogradouroComplemento_PorCep_SemFkLogradouro()
    {
        // CEP sem nenhum logradouro cadastrado — o complemento persiste mesmo assim
        // (não há FK a logradouro). Dois complementos no mesmo CEP coexistem.
        string cep = GeoTestKeys.Cep();
        LogradouroComplemento par = NovoComplemento(cep, "lado par", "lado par");
        LogradouroComplemento impar = NovoComplemento(cep, "lado ímpar", "lado impar");
        LogradouroComplemento parDup = NovoComplemento(cep, "lado par", "lado par");

        await _banco.GravarAsync(par, impar);
        await ExigirDoisComplementosNoCepAsync(cep);
        await _banco.ExigirViolacaoUniqueAsync(parDup, "ux_logradouro_complemento_cep_complemento");
    }

    [Fact(DisplayName = "CA-01: Ativo bool persiste (true e false)")]
    public async Task Logradouro_Ativo_BoolFromSN()
    {
        CidadeSemeada seed = CidadeSemeada.Nova();
        Logradouro ativo = NovoLogradouro(GeoTestKeys.Cep(), "Rua Ativa", "rua ativa", seed.Cidade.Id, ativo: true);
        Logradouro inativo = NovoLogradouro(GeoTestKeys.Cep(), "Rua Inativa", "rua inativa", seed.Cidade.Id, ativo: false);

        await _banco.GravarAsync(seed.EntidadesCom(ativo, inativo));

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        (await leitura.Logradouros.SingleAsync(l => l.Id == ativo.Id)).Ativo.Should().BeTrue();
        (await leitura.Logradouros.SingleAsync(l => l.Id == inativo.Id)).Ativo.Should().BeFalse();
    }

    [Fact(DisplayName = "CA-04: CepGrandeUsuario.cep é único")]
    public async Task CepGrandeUsuario_CepUnico()
    {
        string cep = GeoTestKeys.Cep();
        CepGrandeUsuario g1 = GeoTestKeys.Ok(CepGrandeUsuario.Importar(
            cep, new NomeComFormaDeBusca("Órgão A", null), new ProvenienciaDaCarga(VersaoDataset)));
        CepGrandeUsuario g2 = GeoTestKeys.Ok(CepGrandeUsuario.Importar(
            cep, new NomeComFormaDeBusca("Órgão B", null), new ProvenienciaDaCarga(VersaoDataset)));

        await _banco.GravarAsync(g1);
        await _banco.ExigirViolacaoUniqueAsync(g2, "ix_cep_grande_usuario_cep");
    }

    private static void ConferirLogradouro(Logradouro lido, Guid cidadeId, Guid bairroId)
    {
        lido.Id.Version.Should().Be(7, "EntityBase usa Guid v7 (ADR-0032)");
        lido.CidadeId.Should().Be(cidadeId);
        lido.BairroId.Should().Be(bairroId);
        lido.DistritoId.Should().BeNull();
        lido.Ativo.Should().BeTrue();
        lido.Coordenada.Should().NotBeNull();
        lido.Coordenada!.SRID.Should().Be(4326);
    }

    private async Task ExigirDoisLogradourosNoCepAsync(string cepGeral)
    {
        await using GeoDbContext leitura = _fixture.CreateDbContext();
        int compartilham = await leitura.Logradouros.CountAsync(l => l.Cep == cepGeral);
        compartilham.Should().Be(2, "cep é indexado, não único — vários logradouros compartilham o CEP geral");
    }

    private async Task ExigirDoisComplementosNoCepAsync(string cep)
    {
        await using GeoDbContext leitura = _fixture.CreateDbContext();
        int doCep = await leitura.LogradouroComplementos.CountAsync(c => c.Cep == cep);
        doCep.Should().Be(2, "o mesmo CEP comporta vários complementos, sem vínculo a um logradouro");
    }

    private static LogradouroComplemento NovoComplemento(string cep, string complemento, string complementoNormalizado) =>
        GeoTestKeys.Ok(LogradouroComplemento.Importar(
            cep, complemento, complementoNormalizado, new ProvenienciaDaCarga(VersaoDataset)));

    private static Logradouro NovoLogradouro(string cep, string nome, string nomeNormalizado, Guid cidadeId, bool ativo = true) =>
        GeoTestKeys.Ok(Logradouro.Importar(
            cep,
            new NomeDoLogradouro(null, nome, nome, nomeNormalizado),
            new LocalidadeDoLogradouro(cidadeId, null, null, "SP"),
            default,
            ativo,
            new ProvenienciaDaCarga(VersaoDataset)));
}
