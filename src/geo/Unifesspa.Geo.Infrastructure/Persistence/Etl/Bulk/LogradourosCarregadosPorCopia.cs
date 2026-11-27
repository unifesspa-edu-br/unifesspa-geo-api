namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Bulk;

using System.Runtime.CompilerServices;

using NetTopologySuite.Geometries;

using Npgsql;

using NpgsqlTypes;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Domain.ValueObjects;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Parsing;
using Unifesspa.Geo.Kernel.Results;

/// <summary>
/// Os cerca de 1,4 milhão de logradouros, carregados por cópia binária. A conversão passa pela
/// fábrica do domínio; a cidade é obrigatória (linha sem cidade carregada é órfã e é
/// descartada); distrito e bairro são opcionais e só valem se pertencerem à mesma cidade.
/// </summary>
internal sealed class LogradourosCarregadosPorCopia : ITabelaCarregadaPorCopia<Logradouro>
{
    private const string CriacaoDaTabelaTemporaria = """
        DROP TABLE IF EXISTS logradouro_temporaria;
        CREATE TEMP TABLE logradouro_temporaria (
            id uuid, cep text, tipo text, nome text, nome_completo text, nome_normalizado text,
            cidade_id uuid, distrito_id uuid, bairro_id uuid, uf text,
            latitude numeric(9,6), longitude numeric(9,6), coordenada geography(Point,4326),
            ativo boolean, versao_dataset text, created_at timestamptz, ordem_de_copia bigserial);
        """;

    private const string Copia =
        "COPY logradouro_temporaria (id, cep, tipo, nome, nome_completo, nome_normalizado, cidade_id, distrito_id, bairro_id, uf, latitude, longitude, coordenada, ativo, versao_dataset, created_at) FROM STDIN (FORMAT BINARY)";

    private const string Mesclagem = """
        INSERT INTO logradouro
            (id, cep, tipo, nome, nome_completo, nome_normalizado, cidade_id, distrito_id, bairro_id,
             uf, latitude, longitude, coordenada, ativo, versao_dataset, vigente, created_at, updated_at)
        SELECT DISTINCT ON (cep, nome_normalizado, cidade_id)
            id, cep, tipo, nome, nome_completo, nome_normalizado, cidade_id, distrito_id, bairro_id,
            uf, latitude, longitude, coordenada, ativo, versao_dataset, true, created_at, NULL
        FROM logradouro_temporaria
        ORDER BY cep, nome_normalizado, cidade_id, ordem_de_copia DESC
        ON CONFLICT (cep, nome_normalizado, cidade_id) DO UPDATE SET
            tipo = EXCLUDED.tipo, nome = EXCLUDED.nome, nome_completo = EXCLUDED.nome_completo,
            distrito_id = EXCLUDED.distrito_id, bairro_id = EXCLUDED.bairro_id, uf = EXCLUDED.uf,
            latitude = EXCLUDED.latitude, longitude = EXCLUDED.longitude, coordenada = EXCLUDED.coordenada,
            ativo = EXCLUDED.ativo, versao_dataset = EXCLUDED.versao_dataset,
            vigente = true, updated_at = @now;
        """;

    private readonly IGeoFonteDados _fonte;
    private readonly ResolucaoLocalidades _resolucao;
    private readonly ProvenienciaDaCarga _proveniencia;

    public LogradourosCarregadosPorCopia(IGeoFonteDados fonte, ResolucaoLocalidades resolucao)
    {
        ArgumentNullException.ThrowIfNull(fonte);
        ArgumentNullException.ThrowIfNull(resolucao);

        _fonte = fonte;
        _resolucao = resolucao;
        _proveniencia = new ProvenienciaDaCarga(fonte.Versao);
    }

    public ComandosDaTabelaPorCopia Comandos { get; } = new(
        "logradouro",
        CriacaoDaTabelaTemporaria,
        Copia,
        Mesclagem,
        "DROP TABLE IF EXISTS logradouro_temporaria;");

    public async IAsyncEnumerable<Logradouro> ProjetarAsync(
        ContadorTabela contador,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (LogradouroCru cru in _fonte.LerLogradourosAsync(cancellationToken).ConfigureAwait(false))
        {
            contador.ContarLido();
            Logradouro? logradouro = Converter(cru, contador);
            if (logradouro is not null)
            {
                yield return logradouro;
            }
        }
    }

    public async Task EscreverLinhaAsync(NpgsqlBinaryImporter importador, Logradouro linha, DateTimeOffset instante, CancellationToken cancellationToken)
    {
        await EscreverIdentificacaoAsync(importador, linha, cancellationToken).ConfigureAwait(false);
        await EscreverLocalizacaoAsync(importador, linha, instante, cancellationToken).ConfigureAwait(false);
    }

    private static async Task EscreverIdentificacaoAsync(NpgsqlBinaryImporter importador, Logradouro linha, CancellationToken cancellationToken)
    {
        await importador.WriteAsync(linha.Id, cancellationToken).ConfigureAwait(false);
        await importador.WriteAsync(linha.Cep, cancellationToken).ConfigureAwait(false);
        await EscreverReferenciaOuNuloAsync(importador, linha.Tipo, cancellationToken).ConfigureAwait(false);
        await importador.WriteAsync(linha.Nome, cancellationToken).ConfigureAwait(false);
        await EscreverReferenciaOuNuloAsync(importador, linha.NomeCompleto, cancellationToken).ConfigureAwait(false);
        await importador.WriteAsync(linha.NomeNormalizado, cancellationToken).ConfigureAwait(false);
    }

    private static async Task EscreverLocalizacaoAsync(NpgsqlBinaryImporter importador, Logradouro linha, DateTimeOffset instante, CancellationToken cancellationToken)
    {
        await importador.WriteAsync(linha.CidadeId, cancellationToken).ConfigureAwait(false);
        await EscreverValorOuNuloAsync(importador, linha.DistritoId, cancellationToken).ConfigureAwait(false);
        await EscreverValorOuNuloAsync(importador, linha.BairroId, cancellationToken).ConfigureAwait(false);
        await importador.WriteAsync(linha.Uf, cancellationToken).ConfigureAwait(false);
        await EscreverValorOuNuloAsync(importador, linha.Latitude, cancellationToken).ConfigureAwait(false);
        await EscreverValorOuNuloAsync(importador, linha.Longitude, cancellationToken).ConfigureAwait(false);
        await EscreverPontoAsync(importador, linha.Coordenada, cancellationToken).ConfigureAwait(false);
        await importador.WriteAsync(linha.Ativo, cancellationToken).ConfigureAwait(false);
        await importador.WriteAsync(linha.VersaoDataset, cancellationToken).ConfigureAwait(false);
        await importador.WriteAsync(instante, cancellationToken).ConfigureAwait(false);
    }

    private static async Task EscreverPontoAsync(NpgsqlBinaryImporter importador, Point? ponto, CancellationToken cancellationToken)
    {
        if (ponto is null)
        {
            await importador.WriteNullAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await importador.WriteAsync(ponto, NpgsqlDbType.Geography, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task EscreverReferenciaOuNuloAsync<T>(NpgsqlBinaryImporter importador, T? valor, CancellationToken cancellationToken)
        where T : class
    {
        if (valor is null)
        {
            await importador.WriteNullAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await importador.WriteAsync(valor, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task EscreverValorOuNuloAsync<T>(NpgsqlBinaryImporter importador, T? valor, CancellationToken cancellationToken)
        where T : struct
    {
        if (valor is null)
        {
            await importador.WriteNullAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await importador.WriteAsync(valor.Value, cancellationToken).ConfigureAwait(false);
        }
    }

    private Logradouro? Converter(LogradouroCru cru, ContadorTabela contador)
    {
        // A cidade é obrigatória: linha cuja cidade não foi carregada é descartada e contada.
        if (ResolverCidade(cru) is not Guid cidade)
        {
            contador.ContarOrfao(cru.Cep ?? "(sem cep)");
            return null;
        }

        // Distrito e bairro são opcionais: id ausente ou não resolvido vira nulo, sem descartar a
        // linha. Se a localidade resolvida pertence a outra cidade, também vira nulo e é contada:
        // o domínio delega à carga a coerência hierárquica.
        Guid? distrito = GeoEtlUpsert.ResolverCoerente(_resolucao.DistritosPorIdDne, cru.DistritoIdDne, cidade, contador);
        Guid? bairro = GeoEtlUpsert.ResolverCoerente(_resolucao.BairrosPorIdDne, cru.BairroIdDne, cidade, contador);
        return contador.AceitarOuContarIgnorado(Criar(cru, cidade, distrito, bairro));
    }

    private Guid? ResolverCidade(LogradouroCru cru) =>
        cru.CidadeIdDne is int idCidade && _resolucao.CidadesPorIdDne.TryGetValue(idCidade, out Guid cidade) ? cidade : null;

    // Latitude e longitude só são gravadas dentro de ±90 e ±180 (posição validada): o texto
    // é lido uma só vez e os valores cabem no numeric(9,6) por construção.
    // O nome normalizado é o texto completo sem acento (tipo e nome), que alimenta a busca por
    // texto e compõe a chave de atualização; sem ele na fonte, cai para o texto completo e
    // depois para o nome, que o domínio só põe em minúsculas.
    private Result<Logradouro> Criar(LogradouroCru cru, Guid cidade, Guid? distrito, Guid? bairro) =>
        Logradouro.Importar(
            cru.Cep ?? string.Empty,
            new NomeDoLogradouro(
                cru.Tipo,
                cru.Nome ?? string.Empty,
                cru.NomeCompleto,
                cru.LogradouroSemAcento ?? cru.NomeCompleto ?? cru.Nome ?? string.Empty),
            new LocalidadeDoLogradouro(cidade, distrito, bairro, cru.Uf ?? string.Empty),
            CoordenadaDne.Ler(cru.Latitude, cru.Longitude),
            ParseTolerante.ParaBoolSn(cru.CepAtivo),
            _proveniencia);
}
