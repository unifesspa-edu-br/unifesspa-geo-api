namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;

using System.Data.Common;
using System.Runtime.CompilerServices;

using Npgsql;

using Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

/// <summary>
/// <see cref="IGeoFonteDados"/> que lê o dataset DNE do esquema de carga
/// <c>carga_dne_{execucaoId}</c> do banco do Geo, por <c>SELECT</c> streamado
/// (<see cref="DbDataReader"/> — leitura linha a linha, uso de memória estável mesmo nas tabelas
/// grandes). Cada tabela do esquema leva só o nome da tabela do pacote (<c>cidade_ibge_territorio</c>,
/// no máximo 22 caracteres), sem a versão no nome: a <see cref="Versao"/> é a que o importador
/// carimba como proveniência. Ao ser descartada, apaga o esquema de carga e o arquivo do pacote.
/// </summary>
/// <remarks>
/// <see cref="Versao"/> e o nome do esquema são validados no construtor: só dígitos (AAAAMM) e o
/// nome exato que o esquema de carga produz são aceitos. Todo <c>SELECT</c> sai de
/// <see cref="ComandosDoEsquemaDeCarga"/>, que só aceita colunas obrigatórias da tabela.
/// </remarks>
internal sealed class DneEsquemaDeCargaFonte : IFonteDaCargaDne
{
    private readonly string _connectionString;
    private readonly string _esquema;
    private readonly Func<Task> _descartar;

    public DneEsquemaDeCargaFonte(string connectionString, string versao, string esquema, Func<Task> descartar)
    {
        ArgumentNullException.ThrowIfNull(descartar);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ValidarVersaoEEsquema(versao, esquema);

        _connectionString = connectionString;
        _esquema = esquema;
        _descartar = descartar;
        Versao = versao;
    }

    public string Versao { get; }

    /// <summary>Descarta o esquema de carga e o arquivo do pacote da execução.</summary>
    public async ValueTask DisposeAsync() => await _descartar().ConfigureAwait(false);

    private static void ValidarVersaoEEsquema(string versao, string esquema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versao);
        ArgumentException.ThrowIfNullOrWhiteSpace(esquema);

        if (!VersaoValida(versao))
        {
            throw new ArgumentException(
                $"Versão do dataset inválida: '{versao}'. Esperado AAAAMM (6 dígitos).",
                nameof(versao));
        }

        if (!ComandosDoEsquemaDeCarga.EhNomeValido(esquema))
        {
            throw new ArgumentException("Nome do esquema de carga inválido.", nameof(esquema));
        }
    }

    public IAsyncEnumerable<PaisCru> LerPaisesAsync(CancellationToken cancellationToken) =>
        ConsultarAsync(
            Consultar("paises_ibge", FiltroDeLinhasDoEsquema.Nenhum,
                "sigla_iso", "sigla", "nome_pt", "pais_bcb", "pais_rbf", "pais_sped", "pais_siscomex"),
            static r => new PaisCru(Texto(r, 0), Texto(r, 1), Texto(r, 2), Texto(r, 3), Texto(r, 4), Texto(r, 5), Texto(r, 6)),
            cancellationToken);

    public IAsyncEnumerable<EstadoCru> LerEstadosAsync(CancellationToken cancellationToken) =>
        ConsultarAsync(
            Consultar("estado", FiltroDeLinhasDoEsquema.Nenhum,
                "sigla", "estado", "estado_sem_acento", "regiao", "capital", "faixa_ini", "faixa_fim", "latitude", "longitude"),
            static r => new EstadoCru(Texto(r, 0), Texto(r, 1), Texto(r, 2), Texto(r, 3), Texto(r, 4), Texto(r, 5), Texto(r, 6), Texto(r, 7), Texto(r, 8)),
            cancellationToken);

    public IAsyncEnumerable<EstadoIndicadorCru> LerEstadoIndicadoresAsync(CancellationToken cancellationToken) =>
        ConsultarAsync(
            Consultar("estado_ibge", FiltroDeLinhasDoEsquema.Nenhum,
                "uf", "codigo_ibge", "gentilico", "governador", "area_territorial_km2", "populacao_residente_2022", "densidade_demografica_hab_km2", "matriculas_ensino_fundamental_2023", "idh_indice_desenv_humano", "total_receitas_brutas_realizadas", "total_despesas_brutas_empenhadas", "rendimento_mensal_per_capita", "total_veiculos_2023"),
            static r => new EstadoIndicadorCru(
                Texto(r, 0), Texto(r, 1), Texto(r, 2), Texto(r, 3), Texto(r, 4), Texto(r, 5), Texto(r, 6),
                Texto(r, 7), Texto(r, 8), Texto(r, 9), Texto(r, 10), Texto(r, 11), Texto(r, 12)),
            cancellationToken);

    public IAsyncEnumerable<EstadoFaixaCru> LerEstadoFaixasAsync(CancellationToken cancellationToken) =>
        ConsultarAsync(
            Consultar("estado_faixa", FiltroDeLinhasDoEsquema.Nenhum,
                "sigla", "regiao", "faixa_ini", "faixa_fim"),
            static r => new EstadoFaixaCru(Texto(r, 0), Texto(r, 1), Texto(r, 2), Texto(r, 3)),
            cancellationToken);

    public IAsyncEnumerable<CidadeCru> LerCidadesAsync(CancellationToken cancellationToken) =>
        ConsultarAsync(
            Consultar("cidade", FiltroDeLinhasDoEsquema.Nenhum,
                "cidade_ibge", "cidade", "estado", "ddd", "latitude", "longitude"),
            static r => new CidadeCru(Texto(r, 0), Texto(r, 1), Texto(r, 2), Texto(r, 3), Texto(r, 4), Texto(r, 5)),
            cancellationToken);

    public IAsyncEnumerable<CidadeTerritorioCru> LerCidadeTerritoriosAsync(CancellationToken cancellationToken) =>
        ConsultarAsync(
            Consultar("cidade_ibge_territorio", FiltroDeLinhasDoEsquema.Nenhum,
                "cidade_ibge", "mesorregiao", "mesorregiao_nome", "microrregiao", "microrregiao_nome", "regiao_intermediaria", "regiao_intermediaria_nome", "regiao_geografica_imediata", "regiao_geografica_imediata_nome"),
            static r => new CidadeTerritorioCru(
                Texto(r, 0), Texto(r, 1), Texto(r, 2), Texto(r, 3), Texto(r, 4),
                Texto(r, 5), Texto(r, 6), Texto(r, 7), Texto(r, 8)),
            cancellationToken);

    public IAsyncEnumerable<CidadeIndicadorCru> LerCidadeIndicadoresAsync(CancellationToken cancellationToken) =>
        ConsultarAsync(
            Consultar("cidade_ibge", FiltroDeLinhasDoEsquema.Nenhum,
                "cidade_ibge", "gentilico", "prefeito", "area_territorial_km2", "populacao_residente", "densidade_demografica", "escolarizacao_6_a_14_anos", "idice_de_desenv_humano", "mortalidade_infantil", "receitas_realizadas", "despesas_empenhadas", "pib_per_capita", "aniversario_municipio"),
            static r => new CidadeIndicadorCru(
                Texto(r, 0), Texto(r, 1), Texto(r, 2), Texto(r, 3), Texto(r, 4), Texto(r, 5), Texto(r, 6),
                Texto(r, 7), Texto(r, 8), Texto(r, 9), Texto(r, 10), Texto(r, 11), Texto(r, 12)),
            cancellationToken);

    public IAsyncEnumerable<CidadeFaixaCru> LerCidadeFaixasAsync(CancellationToken cancellationToken) =>
        ConsultarAsync(
            Consultar("cidade_faixa", FiltroDeLinhasDoEsquema.Nenhum,
                "id_cidade", "faixa_ini", "faixa_fim"),
            static r => new CidadeFaixaCru(Inteiro(r, 0), Texto(r, 1), Texto(r, 2)),
            cancellationToken);

    public IAsyncEnumerable<CidadeIdCru> LerCidadeIdsAsync(CancellationToken cancellationToken) =>
        ConsultarAsync(
            Consultar("cidade", FiltroDeLinhasDoEsquema.Nenhum,
                "id_cidade", "cidade_ibge"),
            static r => new CidadeIdCru(Inteiro(r, 0), Texto(r, 1)),
            cancellationToken);

    public IAsyncEnumerable<DistritoCru> LerDistritosAsync(CancellationToken cancellationToken) =>
        ConsultarAsync(
            Consultar("distrito", FiltroDeLinhasDoEsquema.Nenhum,
                "id_distrito", "distrito", "distrito_sem_acento", "cidade_id", "estado", "latitude", "longitude"),
            static r => new DistritoCru(Inteiro(r, 0), Texto(r, 1), Texto(r, 2), Inteiro(r, 3), Texto(r, 4), Texto(r, 5), Texto(r, 6)),
            cancellationToken);

    public IAsyncEnumerable<FaixaLocalidadeCru> LerDistritoFaixasAsync(CancellationToken cancellationToken) =>
        ConsultarAsync(
            Consultar("distrito_faixa", FiltroDeLinhasDoEsquema.Nenhum,
                "id_distrito", "faixa_ini", "faixa_fim"),
            static r => new FaixaLocalidadeCru(Inteiro(r, 0), Texto(r, 1), Texto(r, 2)),
            cancellationToken);

    public IAsyncEnumerable<BairroCru> LerBairrosAsync(CancellationToken cancellationToken) =>
        ConsultarAsync(
            Consultar("bairro", FiltroDeLinhasDoEsquema.Nenhum,
                "id_bairro", "bairro", "bairro_sem_acento", "cidade_id", "estado", "latitude", "longitude"),
            static r => new BairroCru(Inteiro(r, 0), Texto(r, 1), Texto(r, 2), Inteiro(r, 3), Texto(r, 4), Texto(r, 5), Texto(r, 6)),
            cancellationToken);

    public IAsyncEnumerable<FaixaLocalidadeCru> LerBairroFaixasAsync(CancellationToken cancellationToken) =>
        ConsultarAsync(
            Consultar("bairro_faixa", FiltroDeLinhasDoEsquema.Nenhum,
                "id_bairro", "faixa_ini", "faixa_fim"),
            static r => new FaixaLocalidadeCru(Inteiro(r, 0), Texto(r, 1), Texto(r, 2)),
            cancellationToken);

    public IAsyncEnumerable<CepGrandeUsuarioCru> LerCepGrandesUsuariosAsync(CancellationToken cancellationToken) =>
        ConsultarAsync(
            Consultar("log_grande_usuario", FiltroDeLinhasDoEsquema.Nenhum,
                "cep", "grandes_usuarios", "grandes_usuarios_sem_acento"),
            static r => new CepGrandeUsuarioCru(Texto(r, 0), Texto(r, 1), Texto(r, 2)),
            cancellationToken);

    public IAsyncEnumerable<LogradouroComplementoCru> LerLogradouroComplementosAsync(CancellationToken cancellationToken) =>
        ConsultarAsync(
            Consultar("log_complemento", FiltroDeLinhasDoEsquema.Nenhum,
                "cep", "complemento", "complemento_sem_acento"),
            static r => new LogradouroComplementoCru(Texto(r, 0), Texto(r, 1), Texto(r, 2)),
            cancellationToken);

    public IAsyncEnumerable<CepGeralCru> LerCepGeraisAsync(CancellationToken cancellationToken) =>
        ConsultarAsync(
            Consultar("logradouro", FiltroDeLinhasDoEsquema.SemNomeDeLogradouro,
                "cep", "cidade_id", "distrito_id", "estado", "latitude", "longitude"),
            static r => new CepGeralCru(Texto(r, 0), Inteiro(r, 1), Inteiro(r, 2), Texto(r, 3), Texto(r, 4), Texto(r, 5)),
            cancellationToken);

    // Coluna de busca e chave = logradouro_sem_acento (texto completo, ex.: "rua a"), não o nome
    // sem o tipo (ex.: "a").
    public IAsyncEnumerable<LogradouroCru> LerLogradourosAsync(CancellationToken cancellationToken) =>
        ConsultarAsync(
            Consultar("logradouro", FiltroDeLinhasDoEsquema.ComNomeDeLogradouro,
                "cep", "tipo", "nome_logradouro", "logradouro", "bairro_id", "distrito_id", "cidade_id", "estado", "logradouro_sem_acento", "latitude", "longitude", "cep_ativo"),
            static r => new LogradouroCru(
                Texto(r, 0), Texto(r, 1), Texto(r, 2), Texto(r, 3), Texto(r, 8),
                Inteiro(r, 4), Inteiro(r, 5), Inteiro(r, 6),
                Texto(r, 7), Texto(r, 9), Texto(r, 10), Texto(r, 11)),
            cancellationToken);

    private async IAsyncEnumerable<T> ConsultarAsync<T>(
        ComandoDoEsquemaDeCarga comando,
        Func<DbDataReader, T> mapear,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        NpgsqlConnection conexao = new(_connectionString);
        await using (conexao.ConfigureAwait(false))
        {
            await conexao.OpenAsync(cancellationToken).ConfigureAwait(false);

            await foreach (T linha in LerLinhasAsync(conexao, comando, mapear, cancellationToken).ConfigureAwait(false))
            {
                yield return linha;
            }
        }
    }

    private static async IAsyncEnumerable<T> LerLinhasAsync<T>(
        NpgsqlConnection conexao,
        ComandoDoEsquemaDeCarga comando,
        Func<DbDataReader, T> mapear,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        NpgsqlBatch lote = conexao.CreateBatch();
        await using (lote.ConfigureAwait(false))
        {
            lote.BatchCommands.Add(new NpgsqlBatchCommand(comando.Texto));
            DbDataReader leitor = await lote.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await using (leitor.ConfigureAwait(false))
            {
                while (await leitor.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    yield return mapear(leitor);
                }
            }
        }
    }

    private ComandoDoEsquemaDeCarga Consultar(string tabela, FiltroDeLinhasDoEsquema filtro, params string[] colunas) =>
        ComandosDoEsquemaDeCarga.Consultar(_esquema, tabela, filtro, colunas);

    // As colunas de texto do esquema de carga são text — GetString evita o boxing de
    // GetValue().ToString(), relevante no volume dos logradouros.
    private static string? Texto(DbDataReader leitor, int ordinal) =>
        leitor.IsDBNull(ordinal) ? null : leitor.GetString(ordinal);

    // Ids da fonte (id_cidade/id_distrito/id_bairro e as FKs cidade_id/distrito_id/
    // bairro_id) são integer. NULL vira null: a chave opcional do logradouro chega nula com frequência.
    private static int? Inteiro(DbDataReader leitor, int ordinal) =>
        leitor.IsDBNull(ordinal) ? null : leitor.GetInt32(ordinal);

    private static bool VersaoValida(string versao) =>
        versao.Length == 6 && versao.All(char.IsAsciiDigit);
}
