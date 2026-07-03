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
/// <remarks>
/// A validação da connection string acontece em <see cref="Criar"/>, não no construtor: esta
/// classe é resolvida via <c>IGeoImportacaoService</c>/<c>GeoEtlOrquestrador</c>, injetado no
/// <c>GeoImportacoesController</c> tanto para disparar (<c>POST</c>) quanto para consultar
/// status (<c>GET</c>). Validar no construtor faria o DI lançar ao resolver o controller para
/// qualquer requisição — inclusive um <c>GET</c> de status que nunca toca o staging — em
/// ambientes que não configuram <c>GeoStagingDb</c> (a chave não é mais compartilhada com
/// <c>GeoDb</c>, que é sempre obrigatória, então este cenário passou a ser plausível).
/// </remarks>
internal sealed class DneStagingFonteFactory : IGeoFonteDadosFactory
{
    private readonly IConfiguration _configuration;
    private readonly string _schema;

    public DneStagingFonteFactory(IConfiguration configuration, IOptions<EtlOpcoes> opcoes)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(opcoes);

        _configuration = configuration;
        _schema = opcoes.Value.StagingSchema;
    }

    public IGeoFonteDados Criar(string versao)
    {
        string? connectionString = _configuration.GetConnectionString("GeoStagingDb");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:GeoStagingDb não configurada — necessária para o ETL DNE do Geo. "
                + "O appsettings.json base declara a chave vazia (\"\"); defina o valor real via "
                + "appsettings.Development.json ou env var ConnectionStrings__GeoStagingDb.");
        }

        return new DneStagingFonte(connectionString, versao, _schema);
    }
}
