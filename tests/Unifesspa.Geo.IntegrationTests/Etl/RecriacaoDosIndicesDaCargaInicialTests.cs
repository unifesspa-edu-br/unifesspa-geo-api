namespace Unifesspa.Geo.IntegrationTests.Etl;

using AwesomeAssertions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

using Unifesspa.Geo.Infrastructure.Persistence;
using Unifesspa.Geo.Infrastructure.Persistence.Etl;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Bulk;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// A carga inicial do logradouro remove os índices pesados antes da cópia. Se a leitura da fonte
/// falha no meio, os índices precisam voltar a existir e a ser válidos, e o erro que sobe é o
/// original da fonte, não o da recriação (sobre Postgres+PostGIS real).
/// </summary>
[Collection(GeoPostgisCollection.Name)]
public sealed class RecriacaoDosIndicesDaCargaInicialTests
{
    private const string IndiceDoNome = "ix_logradouro_nome_trgm";
    private const string IndiceDaCoordenada = "ix_logradouro_coordenada";

    private readonly GeoPostgisFixture _fixture;

    public RecriacaoDosIndicesDaCargaInicialTests(GeoPostgisFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(DisplayName = "Carga inicial com falha na leitura dos logradouros recria os índices pesados válidos e propaga o erro original")]
    public async Task FalhaNaLeituraDosLogradouros_RecriaOsIndicesEPropagaOErroOriginal()
    {
        InvalidOperationException erroDaFonte = new("Falha simulada ao ler os logradouros do dataset.");
        FonteEmMemoria fonte = new() { FalhaAoLerLogradouros = erroDaFonte };
        EstadoDoIndice nomeAntes = await LerIndiceAsync(IndiceDoNome);
        EstadoDoIndice coordenadaAntes = await LerIndiceAsync(IndiceDaCoordenada);

        Func<Task> carga = () => ExecutarCargaInicialAsync(fonte);

        (await carga.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(erroDaFonte);
        await ConferirIndiceRecriadoAsync(IndiceDoNome, nomeAntes);
        await ConferirIndiceRecriadoAsync(IndiceDaCoordenada, coordenadaAntes);
    }

    private async Task ExecutarCargaInicialAsync(FonteEmMemoria fonte)
    {
        await using GeoDbContext contexto = _fixture.CreateDbContext();
        IConfiguration configuracao = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:GeoDb"] = _fixture.ConnectionString })
            .Build();
        LogradouroCopyImporter importador = new(configuracao, TimeProvider.System, NullLogger<LogradouroCopyImporter>.Instance);
        GeoImportadorLocalidades orquestrador = new(
            new GeoImportadorDistritoBairro(contexto), importador, NullLogger<GeoImportadorLocalidades>.Instance);

        await orquestrador.ImportarAsync(fonte, ModoCarga.Inicial, CancellationToken.None);
    }

    // Um índice recriado ganha identificador novo: igual ao de antes significaria que ele nem
    // chegou a ser removido, e o teste não provaria a recriação.
    private async Task ConferirIndiceRecriadoAsync(string indice, EstadoDoIndice antes)
    {
        EstadoDoIndice depois = await LerIndiceAsync(indice);

        depois.Valido.Should().BeTrue();
        depois.Identificador.Should().NotBe(antes.Identificador);
    }

    private async Task<EstadoDoIndice> LerIndiceAsync(string indice)
    {
        await using NpgsqlConnection conexao = new(_fixture.ConnectionString);
        await conexao.OpenAsync();
        await using NpgsqlCommand comando = conexao.CreateCommand();
        comando.CommandText = "SELECT c.oid::bigint, i.indisvalid FROM pg_index i JOIN pg_class c ON c.oid = i.indexrelid WHERE c.relname = @nome";
        comando.Parameters.AddWithValue("nome", indice);
        await using NpgsqlDataReader leitor = await comando.ExecuteReaderAsync();
        await leitor.ReadAsync();
        return new EstadoDoIndice(leitor.GetInt64(0), leitor.GetBoolean(1));
    }

    private sealed record EstadoDoIndice(long Identificador, bool Valido);
}
