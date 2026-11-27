namespace Unifesspa.Geo.IntegrationTests.Infrastructure;

using System.Data;
using System.Data.Common;

using Microsoft.EntityFrameworkCore;

using Unifesspa.Geo.Infrastructure.Persistence;

/// <summary>
/// Comando aberto sobre uma conexão própria do banco de testes, para consultar o catálogo
/// do Postgres. Libera o comando e o contexto ao terminar.
/// </summary>
internal sealed class ConsultaAoCatalogo : IAsyncDisposable
{
    private readonly GeoDbContext _context;

    public ConsultaAoCatalogo(GeoPostgisFixture fixture)
    {
        _context = fixture.CreateDbContext();
        Comando = _context.Database.GetDbConnection().CreateCommand();
    }

    public DbCommand Comando { get; }

    public async Task AbrirAsync()
    {
        DbConnection connection = _context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }
    }

    public void AdicionarParametro(string nome, object valor)
    {
        DbParameter parametro = Comando.CreateParameter();
        parametro.ParameterName = nome;
        parametro.Value = valor;
        Comando.Parameters.Add(parametro);
    }

    public async ValueTask DisposeAsync()
    {
        await Comando.DisposeAsync();
        await _context.DisposeAsync();
    }
}
