namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Bulk;

using System.Runtime.CompilerServices;

using Npgsql;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;
using Unifesspa.Geo.Kernel.Results;

/// <summary>
/// Os complementos de logradouro por CEP (~235 mil), carregados por cópia binária. A conversão
/// passa pela fábrica do domínio, que é a fonte única da normalização.
/// </summary>
internal sealed class ComplementosCarregadosPorCopia : ITabelaCarregadaPorCopia<LogradouroComplemento>
{
    private const string CriacaoDaTabelaTemporaria = """
        DROP TABLE IF EXISTS logradouro_complemento_temporaria;
        CREATE TEMP TABLE logradouro_complemento_temporaria (
            id uuid, cep text, complemento text, complemento_normalizado text,
            versao_dataset text, created_at timestamptz, ordem_de_copia bigserial);
        """;

    private const string Copia =
        "COPY logradouro_complemento_temporaria (id, cep, complemento, complemento_normalizado, versao_dataset, created_at) FROM STDIN (FORMAT BINARY)";

    private const string Mesclagem = """
        INSERT INTO logradouro_complemento
            (id, cep, complemento, complemento_normalizado, versao_dataset, vigente, created_at, updated_at)
        SELECT DISTINCT ON (cep, complemento_normalizado)
            id, cep, complemento, complemento_normalizado, versao_dataset, true, created_at, NULL
        FROM logradouro_complemento_temporaria
        ORDER BY cep, complemento_normalizado, ordem_de_copia DESC
        ON CONFLICT (cep, complemento_normalizado) DO UPDATE SET
            complemento = EXCLUDED.complemento,
            versao_dataset = EXCLUDED.versao_dataset,
            vigente = true,
            updated_at = @now;
        """;

    private readonly IGeoFonteDados _fonte;

    public ComplementosCarregadosPorCopia(IGeoFonteDados fonte)
    {
        ArgumentNullException.ThrowIfNull(fonte);

        _fonte = fonte;
    }

    public ComandosDaTabelaPorCopia Comandos { get; } = new(
        "logradouro_complemento",
        CriacaoDaTabelaTemporaria,
        Copia,
        Mesclagem,
        "DROP TABLE IF EXISTS logradouro_complemento_temporaria;");

    public async IAsyncEnumerable<LogradouroComplemento> ProjetarAsync(
        ContadorTabela contador,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (LogradouroComplementoCru cru in _fonte.LerLogradouroComplementosAsync(cancellationToken).ConfigureAwait(false))
        {
            contador.ContarLido();
            LogradouroComplemento? complemento = contador.AceitarOuContarIgnorado(Converter(cru));
            if (complemento is not null)
            {
                yield return complemento;
            }
        }
    }

    public async Task EscreverLinhaAsync(NpgsqlBinaryImporter importador, LogradouroComplemento linha, DateTimeOffset instante, CancellationToken cancellationToken)
    {
        await importador.WriteAsync(linha.Id, cancellationToken).ConfigureAwait(false);
        await importador.WriteAsync(linha.Cep, cancellationToken).ConfigureAwait(false);
        await importador.WriteAsync(linha.Complemento, cancellationToken).ConfigureAwait(false);
        await importador.WriteAsync(linha.ComplementoNormalizado, cancellationToken).ConfigureAwait(false);
        await importador.WriteAsync(linha.VersaoDataset, cancellationToken).ConfigureAwait(false);
        await importador.WriteAsync(instante, cancellationToken).ConfigureAwait(false);
    }

    private Result<LogradouroComplemento> Converter(LogradouroComplementoCru cru) =>
        LogradouroComplemento.Importar(
            cru.Cep ?? string.Empty,
            cru.Complemento ?? string.Empty,
            cru.ComplementoSemAcento ?? cru.Complemento ?? string.Empty,
            _fonte.Versao);
}
