namespace Unifesspa.Geo.IntegrationTests.Etl;

using AwesomeAssertions;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.Infrastructure.Persistence.Etl;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

using static Unifesspa.Geo.IntegrationTests.Etl.CenarioDasLocalidades;

/// <summary>
/// Carga das folhas sobre Postgres+PostGIS real: distritos, bairros e faixas por upsert e
/// logradouros e complementos por cópia binária em lote. Cobre a população das tabelas, o CEP
/// geral, o CEP compartilhado, o logradouro de cidade inexistente descartado, os índices pós-carga,
/// o volume e a idempotência da recarga. Cada teste pré-carrega o topo (país, estado e cidade).
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class CargaDasLocalidadesTests
{
    private readonly GeoPostgisFixture _fixture;
    private readonly CenarioDasLocalidades _cenario;

    public CargaDasLocalidadesTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
        _cenario = new CenarioDasLocalidades(fixture);
    }

    [Fact(DisplayName = "CA-01/02: COPY popula logradouros com FK Guid resolvida, ativo bool e coordenada 4326")]
    public async Task CargaInicial_PopulaLogradouros()
    {
        await _cenario.PrepararTopoAsync(FonteCompleta());
        await _cenario.ExecutarLocalidadesAsync(FonteCompleta());

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        Guid marabaId = (await leitura.Cidades.SingleAsync(c => c.CodigoIbge == CodigoMaraba)).Id;

        (await leitura.Distritos.CountAsync()).Should().Be(2);
        (await leitura.Bairros.CountAsync()).Should().Be(2);
        (await leitura.DistritoFaixasCep.CountAsync()).Should().Be(1);
        (await leitura.BairroFaixasCep.CountAsync()).Should().Be(1);
        (await leitura.CepGrandesUsuarios.CountAsync()).Should().Be(1);
        (await leitura.LogradouroComplementos.CountAsync()).Should().Be(2);

        Logradouro ruaA = await leitura.Logradouros.SingleAsync(l => l.Cep == "68500000" && l.Nome == "Rua A");
        ruaA.CidadeId.Should().Be(marabaId);
        ruaA.DistritoId.Should().NotBeNull(); // distrito_id 10 resolvido para Guid
        ruaA.BairroId.Should().NotBeNull();
        ruaA.Ativo.Should().BeTrue();          // cep_ativo 'S'
        ruaA.Coordenada.Should().NotBeNull();
        ruaA.Coordenada!.SRID.Should().Be(4326);

        Logradouro ruaB = await leitura.Logradouros.SingleAsync(l => l.Nome == "Rua B");
        ruaB.DistritoId.Should().BeNull();     // distrito_id nulo aceito (FK opcional)
        ruaB.Ativo.Should().BeFalse();         // cep_ativo 'N'
    }

    [Fact(DisplayName = "CEP geral: linha sem nome vira cep_geral de cidade ou de distrito, com coordenada e FKs resolvidas")]
    public async Task CargaInicial_PopulaCepGeral()
    {
        FonteEmMemoria fonte = FonteComCepGeral();
        await _cenario.PrepararTopoAsync(fonte);

        RelatorioImportacao relatorio = await _cenario.ExecutarLocalidadesAsync(fonte);

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        Guid marabaId = (await leitura.Cidades.SingleAsync(c => c.CodigoIbge == CodigoMaraba)).Id;
        Guid distritoId = (await leitura.Distritos.OrderBy(d => d.Nome).FirstAsync(d => d.IdOrigemDne == "10")).Id;

        CepGeral cidade = await leitura.CepGerais.SingleAsync(g => g.Cep == "68525000");
        cidade.CidadeId.Should().Be(marabaId);
        cidade.DistritoId.Should().BeNull();
        cidade.Coordenada.Should().NotBeNull();
        cidade.Coordenada!.SRID.Should().Be(4326);

        CepGeral distrito = await leitura.CepGerais.SingleAsync(g => g.Cep == "68525100");
        distrito.DistritoId.Should().Be(distritoId);

        (await leitura.CepGerais.CountAsync()).Should().Be(2);
        relatorio.Tabela("cep_geral").Inseridos.Should().Be(2);
    }

    [Fact(DisplayName = "CEP geral com distrito de outra cidade é rebaixado a CEP geral da cidade e a degradação é contada")]
    public async Task CepGeral_DistritoDeOutraCidade_RebaixaParaCidade()
    {
        FonteEmMemoria fonte = FonteCompleta();
        // Distrito 20 pertence a São Paulo; o CEP geral declara Marabá.
        fonte.CepGerais.Add(DadosDne.CepGeral("68525200", IdCidadeMaraba, distritoIdDne: 20));
        await _cenario.PrepararTopoAsync(fonte);

        RelatorioImportacao relatorio = await _cenario.ExecutarLocalidadesAsync(fonte);

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        Guid marabaId = (await leitura.Cidades.SingleAsync(c => c.CodigoIbge == CodigoMaraba)).Id;
        CepGeral geral = await leitura.CepGerais.SingleAsync(g => g.Cep == "68525200");
        geral.CidadeId.Should().Be(marabaId);
        geral.DistritoId.Should().BeNull();
        relatorio.Tabela("cep_geral").ParsesDegradados.Should().Be(1);
    }

    [Fact(DisplayName = "CEP geral: recarga é idempotente — não duplica e conta como atualizado")]
    public async Task CepGeral_RecargaIdempotente()
    {
        FonteEmMemoria fonte = FonteComCepGeral();
        await _cenario.PrepararTopoAsync(fonte);
        await _cenario.ExecutarLocalidadesAsync(fonte);

        RelatorioImportacao relatorio = await _cenario.ExecutarLocalidadesAsync(fonte);

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        (await leitura.CepGerais.CountAsync()).Should().Be(2);
        relatorio.Tabela("cep_geral").Inseridos.Should().Be(0);
        relatorio.Tabela("cep_geral").Atualizados.Should().Be(2);
    }

    [Fact(DisplayName = "CA-03/04: após a carga, os índices trigram e GIST permanecem válidos")]
    public async Task Carga_MantemIndicesPesadosValidos()
    {
        await _cenario.PrepararTopoAsync(FonteCompleta());

        await _cenario.ExecutarLocalidadesAsync(FonteCompleta());

        (await IndiceValidoAsync("ix_logradouro_nome_trgm")).Should().BeTrue();
        (await IndiceValidoAsync("ix_logradouro_coordenada")).Should().BeTrue();
    }

    [Fact(DisplayName = "CA-05: CEP geral é compartilhado por vários logradouros (cep indexado, não único)")]
    public async Task CepCompartilhado_Coexiste()
    {
        await _cenario.PrepararTopoAsync(FonteCompleta());
        FonteEmMemoria fonte = FonteCompleta();
        fonte.Logradouros.Add(DadosDne.Logradouro("68500000", "Rua C", IdCidadeMaraba)); // mesmo CEP de "Rua A"

        await _cenario.ExecutarLocalidadesAsync(fonte);

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        (await leitura.Logradouros.CountAsync(l => l.Cep == "68500000")).Should().Be(2);
    }

    [Fact(DisplayName = "CA-05: logradouro com cidade órfã é descartado e contado; distrito_id nulo é aceito")]
    public async Task LogradouroOrfao_Descartado()
    {
        await _cenario.PrepararTopoAsync(FonteCompleta());
        FonteEmMemoria fonte = FonteCompleta();
        fonte.Logradouros.Add(DadosDne.Logradouro("99999999", "Rua Fantasma", IdCidadeInexistente));

        RelatorioImportacao relatorio = await _cenario.ExecutarLocalidadesAsync(fonte);

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        (await leitura.Logradouros.AnyAsync(l => l.Cep == "99999999")).Should().BeFalse();
        relatorio.Tabelas["logradouro"].Orfaos.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact(DisplayName = "CA-07: recarga (staging + ON CONFLICT) é idempotente — contagens idênticas, Id e created_at preservados, updated_at carimbado")]
    public async Task Recarga_Idempotente()
    {
        await _cenario.PrepararTopoAsync(FonteCompleta());
        await _cenario.ExecutarLocalidadesAsync(FonteCompleta());

        Guid logradouroId;
        DateTimeOffset createdAt;
        int totalLogradouros;
        await using (GeoDbContext antes = _fixture.CreateDbContext())
        {
            Logradouro ruaA = await antes.Logradouros.SingleAsync(l => l.Nome == "Rua A");
            logradouroId = ruaA.Id;
            createdAt = ruaA.CreatedAt;
            totalLogradouros = await antes.Logradouros.CountAsync();
        }

        await _cenario.ExecutarLocalidadesAsync(FonteCompleta());

        await using GeoDbContext depois = _fixture.CreateDbContext();
        (await depois.Logradouros.CountAsync()).Should().Be(totalLogradouros, "a recarga idempotente não pode duplicar");
        (await depois.LogradouroComplementos.CountAsync()).Should().Be(2);

        Logradouro ruaARecarga = await depois.Logradouros.SingleAsync(l => l.Nome == "Rua A");
        ruaARecarga.Id.Should().Be(logradouroId, "ON CONFLICT preserva a PK");
        ruaARecarga.CreatedAt.Should().Be(createdAt, "created_at é preservado no conflito");
        ruaARecarga.UpdatedAt.Should().NotBeNull("updated_at é carimbado no DO UPDATE");
    }

    [Fact(DisplayName = "CA-02: COPY em lote suporta volume — várias linhas streamadas entram sem duplicar")]
    public async Task CopyEmLote_SuportaVolume()
    {
        await _cenario.PrepararTopoAsync(FonteCompleta());
        FonteEmMemoria fonte = FonteCompleta();
        fonte.Logradouros.Clear();
        for (int i = 0; i < 250; i++)
        {
            fonte.Logradouros.Add(DadosDne.Logradouro(
                cep: 68500000.ToString(System.Globalization.CultureInfo.InvariantCulture),
                nome: $"Rua {i.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                cidadeIdDne: IdCidadeMaraba));
        }

        await _cenario.ExecutarLocalidadesAsync(fonte);

        await using GeoDbContext leitura = _fixture.CreateDbContext();
        (await leitura.Logradouros.CountAsync()).Should().Be(250);
    }

    [Fact(DisplayName = "CA-08: o relatório reporta totais por tabela (lidos/inseridos) das folhas")]
    public async Task Relatorio_ReportaTotais()
    {
        await _cenario.PrepararTopoAsync(FonteCompleta());
        RelatorioImportacao relatorio = await _cenario.ExecutarLocalidadesAsync(FonteCompleta());

        relatorio.Tabelas["distrito"].Lidos.Should().Be(2);
        relatorio.Tabelas["bairro"].Lidos.Should().Be(2);
        relatorio.Tabelas["logradouro"].Lidos.Should().Be(2);
        relatorio.Tabelas["logradouro"].Inseridos.Should().Be(2);
        relatorio.Tabelas["logradouro_complemento"].Inseridos.Should().Be(2);
    }

    // indisvalid = false sinaliza um CREATE INDEX CONCURRENTLY que não concluiu; true
    // prova que a recriação fora de transação completou.
    private async Task<bool> IndiceValidoAsync(string indice)
    {
        await using NpgsqlConnection conexao = new(_fixture.ConnectionString);
        await conexao.OpenAsync();
        await using NpgsqlCommand comando = conexao.CreateCommand();
        comando.CommandText = "SELECT i.indisvalid FROM pg_index i JOIN pg_class c ON c.oid = i.indexrelid WHERE c.relname = @nome";
        comando.Parameters.AddWithValue("nome", indice);
        object? resultado = await comando.ExecuteScalarAsync();
        return resultado is bool valido && valido;
    }
}
