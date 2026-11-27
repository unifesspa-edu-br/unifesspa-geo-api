namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

using System.Collections.Frozen;

/// <summary>
/// As 15 tabelas do pacote dos Correios e as colunas que a carga exige de cada uma, com o
/// tipo esperado. É a única fonte da verdade para a conferência do zip, a leitura dos
/// arquivos e o esquema de carga; colunas extras no arquivo são toleradas.
/// </summary>
internal static class TabelasDne
{
    private static readonly FrozenDictionary<string, IReadOnlyList<ColunaDne>> Esquemas =
        new Dictionary<string, IReadOnlyList<ColunaDne>>
        {
            ["paises_ibge"] = Colunas("sigla_iso sigla nome_pt pais_bcb pais_rbf pais_sped pais_siscomex"),
            ["estado"] = Colunas("sigla estado estado_sem_acento regiao capital faixa_ini faixa_fim latitude longitude"),
            ["estado_ibge"] = Colunas(
                "uf codigo_ibge gentilico governador area_territorial_km2 populacao_residente_2022 "
                + "densidade_demografica_hab_km2 matriculas_ensino_fundamental_2023 idh_indice_desenv_humano "
                + "total_receitas_brutas_realizadas total_despesas_brutas_empenhadas rendimento_mensal_per_capita "
                + "total_veiculos_2023"),
            ["estado_faixa"] = Colunas("sigla regiao faixa_ini faixa_fim"),
            ["cidade"] = Colunas("cidade_ibge cidade estado ddd latitude longitude", "id_cidade"),
            ["cidade_ibge_territorio"] = Colunas(
                "cidade_ibge mesorregiao mesorregiao_nome microrregiao microrregiao_nome regiao_intermediaria "
                + "regiao_intermediaria_nome regiao_geografica_imediata regiao_geografica_imediata_nome"),
            ["cidade_ibge"] = Colunas(
                "cidade_ibge gentilico prefeito area_territorial_km2 populacao_residente densidade_demografica "
                + "escolarizacao_6_a_14_anos idice_de_desenv_humano mortalidade_infantil receitas_realizadas "
                + "despesas_empenhadas pib_per_capita aniversario_municipio"),
            ["cidade_faixa"] = Colunas("faixa_ini faixa_fim", "id_cidade"),
            ["distrito"] = Colunas("distrito distrito_sem_acento estado latitude longitude", "id_distrito cidade_id"),
            ["distrito_faixa"] = Colunas("faixa_ini faixa_fim", "id_distrito"),
            ["bairro"] = Colunas("bairro bairro_sem_acento estado latitude longitude", "id_bairro cidade_id"),
            ["bairro_faixa"] = Colunas("faixa_ini faixa_fim", "id_bairro"),
            ["log_grande_usuario"] = Colunas("cep grandes_usuarios grandes_usuarios_sem_acento"),
            ["log_complemento"] = Colunas("cep complemento complemento_sem_acento"),
            ["logradouro"] = Colunas(
                "cep tipo nome_logradouro logradouro estado logradouro_sem_acento latitude longitude cep_ativo",
                "bairro_id distrito_id cidade_id"),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly string[] NomesEmOrdemAlfabetica = [.. Esquemas.Keys.Order(StringComparer.Ordinal)];

    /// <summary>Os 15 nomes de tabela (sufixo de <c>tbl_cep_{versao}_n_{nome}</c>), em ordem alfabética.</summary>
    public static IReadOnlyList<string> Nomes => NomesEmOrdemAlfabetica;

    public static bool Contem(string nome) => Esquemas.ContainsKey(nome);

    /// <summary>Colunas que a carga exige da tabela, com o tipo esperado.</summary>
    public static IReadOnlyList<ColunaDne> ColunasObrigatorias(string tabela) =>
        Esquemas.TryGetValue(tabela, out IReadOnlyList<ColunaDne>? colunas)
            ? colunas
            : throw new ArgumentException("Tabela fora da lista do pacote.", nameof(tabela));

    private static List<ColunaDne> Colunas(string textos, string inteiros = "") =>
    [
        .. Separar(textos).Select(nome => new ColunaDne(nome, TipoDeColunaDne.Texto)),
        .. Separar(inteiros).Select(nome => new ColunaDne(nome, TipoDeColunaDne.Inteiro)),
    ];

    private static string[] Separar(string nomes) =>
        nomes.Split(' ', StringSplitOptions.RemoveEmptyEntries);
}
