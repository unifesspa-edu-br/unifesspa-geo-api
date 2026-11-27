namespace Unifesspa.Geo.IntegrationTests.Etl;

using AwesomeAssertions;

using Microsoft.EntityFrameworkCore;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

using static Unifesspa.Geo.IntegrationTests.Etl.CenarioDasLocalidades;

/// <summary>
/// Chave natural dos logradouros (CEP, texto completo normalizado e cidade) sobre
/// Postgres+PostGIS real: a reconciliação da chave antiga, a rotação de chaves, a escolha da linha
/// vigente na remoção de duplicados e a convivência de homônimos que só diferem pelo tipo.
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class ChaveDosLogradourosNoBackfillTests
{
    // Espelha a migration ReconciliaNomeNormalizadoLogradouroTextoCompleto: dropa o
    // índice único, recalcula nome_normalizado para o texto completo sem acento, deduplica
    // colisões reais (vigente DESC, versao_dataset DESC, id DESC) e recria o índice. Mantido
    // idêntico ao SQL da migration.
    private const string BackfillSql = """
        DROP INDEX IF EXISTS ix_logradouro_natural;
        UPDATE logradouro SET nome_normalizado = lower(translate(coalesce(nome_completo, nome), 'àáâãäçèéêëìíîïñòóôõöùúûüÀÁÂÃÄÇÈÉÊËÌÍÎÏÑÒÓÔÕÖÙÚÛÜ', 'aaaaaceeeeiiiinooooouuuuAAAAACEEEEIIIINOOOOOUUUU'));
        DELETE FROM logradouro l USING (SELECT id, row_number() OVER (PARTITION BY cep, nome_normalizado, cidade_id ORDER BY vigente DESC, versao_dataset DESC, id DESC) AS rn FROM logradouro) ranked WHERE l.id = ranked.id AND ranked.rn > 1;
        CREATE UNIQUE INDEX ix_logradouro_natural ON logradouro (cep, nome_normalizado, cidade_id);
        """;

    private const string IdDaLinhaVigente = "01000000-0000-7000-8000-000000000001";
    private const string IdDaLinhaNaoVigente = "01000000-0000-7000-8000-000000000002"; // id MAIOR que a vigente

    private readonly GeoPostgisFixture _fixture;
    private readonly CenarioDasLocalidades _cenario;

    public ChaveDosLogradourosNoBackfillTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _cenario = new CenarioDasLocalidades(fixture);
    }

    [Fact(DisplayName = "#707: backfill reconcilia chave legada (name-only) para texto completo; recarga de mesma versão não duplica")]
    public async Task Backfill_ReconciliaChaveLegada_RecargaMesmaVersaoNaoDuplica()
    {
        await _cenario.PrepararTopoAsync(FonteCompleta());
        FonteEmMemoria fonte = FonteComPracaDaSeEmChaveLegada();
        await _cenario.ExecutarLocalidadesAsync(fonte);
        await RebaixarParaChaveSomenteNomeAsync();
        await AplicarBackfillAsync();
        await ConferirNomeNormalizadoDoUnicoLogradouroAsync("praca da se");

        // Recarga da MESMA versão: agora o ON CONFLICT casa a chave reconciliada e atualiza
        // a linha em vez de inserir uma duplicata vigente.
        await _cenario.ExecutarLocalidadesAsync(fonte);

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        (await leitura.Logradouros.CountAsync(l => l.Cep == "68500000")).Should().Be(1);
        (await leitura.Logradouros.SingleAsync(l => l.Cep == "68500000")).NomeNormalizado.Should().Be("praca da se");
    }

    [Fact(DisplayName = "#707: backfill não aborta com rotação de chave (texto completo de um == name-only de outro no mesmo CEP)")]
    public async Task Backfill_RotacaoDeChave_NaoAbortaPelaUnique()
    {
        await _cenario.PrepararTopoAsync(FonteCompleta());
        await _cenario.ExecutarLocalidadesAsync(FonteComRotacaoDeChaves());
        await RebaixarParaChaveSomenteNomeComRotacaoAsync();

        // O backfill (drop índice → recalcula → recria) conclui sem abortar pela colisão
        // transitória e deixa as duas chaves finais corretas.
        await AplicarBackfillAsync();

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        List<Logradouro> doCep = await leitura.Logradouros.Where(l => l.Cep == "68500000").ToListAsync();
        doCep.Should().HaveCount(2);
        doCep.Select(l => l.NomeNormalizado).Should().BeEquivalentTo(["rua a", "avenida rua a"]);
    }

    [Fact(DisplayName = "#707: dedup do backfill preserva a linha vigente, não a linha não vigente de id maior")]
    public async Task Backfill_Dedup_PreservaVigenteSobreNaoVigente()
    {
        await _cenario.PrepararTopoAsync(FonteCompleta());
        await InserirLinhasQueConvergemParaOMesmoNomeAsync();

        await AplicarBackfillAsync();

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        Logradouro sobrevivente = await leitura.Logradouros.SingleAsync(l => l.Cep == "68500000");
        sobrevivente.Id.Should().Be(Guid.Parse(IdDaLinhaVigente), "a linha vigente vence a não vigente de id maior");
        sobrevivente.Vigente.Should().BeTrue();
        sobrevivente.NomeNormalizado.Should().Be("rua a");
    }

    [Fact(DisplayName = "CA-05 (#707): logradouros homônimos sob o mesmo CEP-geral coexistem — o texto completo distingue a chave de upsert")]
    public async Task CepGeral_HomonimosPorTipo_CoexistemPorTextoCompleto()
    {
        await _cenario.PrepararTopoAsync(FonteCompleta());
        FonteEmMemoria fonte = FonteComHomonimosPorTipoSobOMesmoCep();

        await _cenario.ExecutarLocalidadesAsync(fonte);

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        List<Logradouro> doCep = await leitura.Logradouros.Where(l => l.Cep == "68500500").ToListAsync();
        doCep.Should().HaveCount(2);
        doCep.Select(l => l.NomeNormalizado).Should().BeEquivalentTo(["rua das flores", "travessa das flores"]);
    }

    private static FonteEmMemoria FonteComPracaDaSeEmChaveLegada()
    {
        FonteEmMemoria fonte = FonteCompleta();
        fonte.Logradouros.Clear();
        // Tipo e nome em colunas distintas: o name-only ("se") difere do texto completo
        // ("praca da se"), reproduzindo o que distingue a chave antiga da nova.
        fonte.Logradouros.Add(DadosDne.Logradouro(
            "68500000", "Sé", IdCidadeMaraba, tipo: "Praça",
            nomeCompleto: "Praça da Sé", logradouroSemAcento: "praca da se"));
        return fonte;
    }

    private static FonteEmMemoria FonteComRotacaoDeChaves()
    {
        FonteEmMemoria fonte = FonteCompleta();
        fonte.Logradouros.Clear();
        // Rotação: o texto completo de A ("rua a") coincide com o name-only de B ("Rua A"
        // sem o tipo). Sob a chave antiga (name-only) e a nova (texto completo) ambos são
        // válidos, mas um UPDATE de uma passada colidiria no instante intermediário.
        fonte.Logradouros.Add(DadosDne.Logradouro(
            "68500000", "A", IdCidadeMaraba, tipo: "Rua",
            nomeCompleto: "Rua A", logradouroSemAcento: "rua a"));
        fonte.Logradouros.Add(DadosDne.Logradouro(
            "68500000", "Rua A", IdCidadeMaraba, tipo: "Avenida",
            nomeCompleto: "Avenida Rua A", logradouroSemAcento: "avenida rua a"));
        return fonte;
    }

    private static FonteEmMemoria FonteComHomonimosPorTipoSobOMesmoCep()
    {
        FonteEmMemoria fonte = FonteCompleta();
        fonte.Logradouros.Clear();

        // Mesmo CEP-geral, mesmo nome sem o tipo ("das Flores"); só o tipo — logo, o texto
        // completo — difere. Com a antiga chave name-only um seria descartado pelo DISTINCT ON;
        // com o texto completo (nome_normalizado) ambos coexistem.
        fonte.Logradouros.Add(DadosDne.Logradouro(
            "68500500", "das Flores", IdCidadeMaraba, tipo: "Rua",
            nomeCompleto: "Rua das Flores", logradouroSemAcento: "rua das flores"));
        fonte.Logradouros.Add(DadosDne.Logradouro(
            "68500500", "das Flores", IdCidadeMaraba, tipo: "Travessa",
            nomeCompleto: "Travessa das Flores", logradouroSemAcento: "travessa das flores"));
        return fonte;
    }

    // Rebaixa nome_normalizado para a forma name-only que a versão anterior do ETL
    // gravava a partir de nome_logradouro_sem_acento ("Sé" → "se") — o estado legado
    // que a migration precisa reconciliar.
    private async Task RebaixarParaChaveSomenteNomeAsync()
    {
        await using GeoDbContext ctx = _fixture.CreateDbContext();
        await ctx.Database.ExecuteSqlRawAsync("UPDATE logradouro SET nome_normalizado = 'se'");
        (await ctx.Logradouros.SingleAsync()).NomeNormalizado.Should().Be("se");
    }

    // Estado legado name-only: A → "a", B → "rua a" (rotaciona com o futuro texto de A).
    private async Task RebaixarParaChaveSomenteNomeComRotacaoAsync()
    {
        await using GeoDbContext ctx = _fixture.CreateDbContext();
        await ctx.Database.ExecuteSqlRawAsync("UPDATE logradouro SET nome_normalizado = 'a' WHERE nome = 'A'");
        await ctx.Database.ExecuteSqlRawAsync("UPDATE logradouro SET nome_normalizado = 'rua a' WHERE nome = 'Rua A'");
    }

    // Aplica o backfill da migration: reconcilia para o texto completo sem acento.
    private async Task AplicarBackfillAsync()
    {
        await using GeoDbContext ctx = _fixture.CreateDbContext();
        await ctx.Database.ExecuteSqlRawAsync(BackfillSql);
    }

    private async Task ConferirNomeNormalizadoDoUnicoLogradouroAsync(string esperado)
    {
        await using GeoDbContext ctx = _fixture.CreateDbContext();
        (await ctx.Logradouros.SingleAsync()).NomeNormalizado.Should().Be(esperado);
    }

    private async Task InserirLinhasQueConvergemParaOMesmoNomeAsync()
    {
        await using GeoDbContext ctx = _fixture.CreateDbContext();
        Guid cidadeId = (await ctx.Cidades.SingleAsync(c => c.CodigoIbge == CodigoMaraba)).Id;

        // Duas linhas que convergem para "rua a" após o recálculo (mesmo nome_completo),
        // com chaves legadas distintas ("a" e "rua a"). A linha não vigente (vigente=false) tem id
        // MAIOR — a regra ingênua "maior id vence" manteria a errada, escondendo o
        // endereço dos filtros read-side por vigente=true.
        string sql = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "INSERT INTO logradouro (id, cep, tipo, nome, nome_completo, nome_normalizado, cidade_id, uf, ativo, versao_dataset, vigente, created_at) VALUES " +
            "('{0}', '68500000', 'Rua', 'A', 'Rua A', 'a', '{2}', 'PA', true, '202601', true, now()), " +
            "('{1}', '68500000', 'Avenida', 'Rua A', 'Rua A', 'rua a', '{2}', 'PA', true, '202512', false, now());",
            IdDaLinhaVigente, IdDaLinhaNaoVigente, cidadeId);
        await ctx.Database.ExecuteSqlRawAsync(sql);
    }
}
