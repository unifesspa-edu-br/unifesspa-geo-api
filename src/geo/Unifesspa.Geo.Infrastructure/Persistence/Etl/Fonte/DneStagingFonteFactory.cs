namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

/// <summary>
/// <see cref="IGeoFonteDadosFactory"/> de produção: constrói uma <see cref="DneStagingFonte"/>
/// sobre o banco de staging <strong>dedicado</strong> da DNE (issue #12) — independente do
/// banco de domínio (<c>GeoDb</c>). A connection string vem da
/// <c>ConnectionStrings:GeoStagingDb</c> — <strong>nunca</strong> de
/// <c>DbContext.Database.GetConnectionString()</c>, que o Npgsql devolve sem a senha após
/// a primeira abertura (lição da Story #673).
/// </summary>
internal sealed class DneStagingFonteFactory : IGeoFonteDadosFactory
{
    private readonly string _connectionString;
    private readonly string _schema;

    public DneStagingFonteFactory(IConfiguration configuration, IOptions<EtlOpcoes> opcoes)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(opcoes);

        string? connectionString = configuration.GetConnectionString("GeoStagingDb");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:GeoStagingDb não configurada — necessária para o ETL DNE do Geo. "
                + "O appsettings.json base declara a chave vazia (\"\"); defina o valor real via "
                + "appsettings.Development.json ou env var ConnectionStrings__GeoStagingDb.");
        }

        _connectionString = connectionString;
        _schema = opcoes.Value.StagingSchema;
    }

    public IGeoFonteDados Criar(string versao) => new DneStagingFonte(_connectionString, versao, _schema);
}
