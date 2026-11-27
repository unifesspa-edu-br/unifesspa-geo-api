namespace Unifesspa.Geo.IntegrationTests.Etl.Pacote;

using System.Globalization;
using System.IO.Compression;

using Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

/// <summary>
/// Monta, no próprio teste, um zip com as 15 tabelas no formato dos Correios e valores fictícios
/// (nenhum dado real nem pessoal). As colunas de cada tabela são as obrigatórias de
/// <see cref="TabelasDne"/>; a coluna não informada numa linha vira NULL.
/// </summary>
internal sealed class PacoteSinteticoDne
{
    private const string ValorDaColunaExtra = "'extra'";

    private readonly Dictionary<string, List<LinhaDoPacote>> _insertsPorTabela =
        TabelasDne.Nomes.ToDictionary(tabela => tabela, _ => new List<LinhaDoPacote>(), StringComparer.Ordinal);

    private readonly Dictionary<string, string> _colunaExtraPorTabela = new(StringComparer.Ordinal);

    public PacoteSinteticoDne(string versao)
    {
        Versao = versao;
    }

    public string Versao { get; }

    /// <summary>Pacote coerente com uma cidade (Marabá), um distrito e dois logradouros.</summary>
    public static PacoteSinteticoDne Completo(string versao) => new PacoteSinteticoDne(versao)
        .Com("paises_ibge", ("sigla_iso", "BRA"), ("sigla", "BR"), ("nome_pt", "Brasil"))
        .Com("estado", ("sigla", "PA"), ("estado", "Pará"), ("estado_sem_acento", "Para"), ("regiao", "Norte"), ("capital", "Belém"), ("faixa_ini", "66000000"), ("faixa_fim", "68899999"))
        .Com("estado_ibge", ("uf", "PA"), ("codigo_ibge", "15"))
        .Com("estado_faixa", ("sigla", "PA"), ("regiao", "Norte"), ("faixa_ini", "66000000"), ("faixa_fim", "68899999"))
        .Com("cidade", ("id_cidade", 1), ("cidade_ibge", "1500402"), ("cidade", "Marabá"), ("estado", "PA"), ("ddd", "94"), ("latitude", "-5.36867"), ("longitude", "-49.11731"))
        .Com("cidade_ibge_territorio", ("cidade_ibge", "1500402"), ("mesorregiao_nome", "Sudeste Paraense"))
        .Com("cidade_ibge", ("cidade_ibge", "1500402"), ("gentilico", "marabaense"))
        .Com("cidade_faixa", ("id_cidade", 1), ("faixa_ini", "68500000"), ("faixa_fim", "68519999"))
        .Com("distrito", ("id_distrito", 10), ("distrito", "Cidade Nova"), ("distrito_sem_acento", "Cidade Nova"), ("cidade_id", 1), ("estado", "PA"))
        .Com("distrito_faixa", ("id_distrito", 10), ("faixa_ini", "68500000"), ("faixa_fim", "68519999"))
        .Com("log_complemento", ("cep", "68500000"), ("complemento", "lado par"), ("complemento_sem_acento", "lado par"))
        .Com("logradouro", ("cep", "68500000"), ("tipo", "Rua"), ("nome_logradouro", "A"), ("logradouro", "Rua A"), ("logradouro_sem_acento", "Rua A"), ("estado", "PA"), ("cidade_id", 1), ("distrito_id", 10), ("latitude", "-5.36867"), ("longitude", "-49.11731"), ("cep_ativo", "S"))
        .Com("logradouro", ("cep", "68500001"), ("tipo", "Rua"), ("nome_logradouro", "B"), ("logradouro", "Rua B"), ("logradouro_sem_acento", "Rua B"), ("estado", "PA"), ("cidade_id", 1), ("cep_ativo", "S"));

    /// <summary>Declara, no fim do <c>CREATE TABLE</c> da tabela, uma coluna de texto que a carga não usa.</summary>
    public PacoteSinteticoDne ComColunaExtra(string tabela, string nome)
    {
        _colunaExtraPorTabela[tabela] = nome;
        return this;
    }

    public PacoteSinteticoDne Com(string tabela, params (string Coluna, object? Valor)[] valores)
    {
        string texto = string.Join(", ", TabelasDne.ColunasObrigatorias(tabela).Select(c => Literal(valores, c.Nome)));
        _insertsPorTabela[tabela].Add(new LinhaDoPacote(texto, Montada: true));
        return this;
    }

    /// <summary>Acrescenta um INSERT com o texto de valores exatamente como informado (para casos malformados).</summary>
    public PacoteSinteticoDne ComLinhaBruta(string tabela, string valoresBrutos)
    {
        _insertsPorTabela[tabela].Add(new LinhaDoPacote(valoresBrutos, Montada: false));
        return this;
    }

    public byte[] MontarZip()
    {
        using MemoryStream saida = new();
        using (ZipArchive zip = new(saida, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (string tabela in TabelasDne.Nomes)
            {
                AdicionarEntrada(zip, tabela);
            }
        }

        return saida.ToArray();
    }

    /// <summary>Grava o zip no diretório com o identificador da execução como nome; devolve o caminho.</summary>
    public string Gravar(string diretorio, Guid execucaoId)
    {
        Directory.CreateDirectory(diretorio);
        string caminho = Path.Combine(diretorio, execucaoId.ToString("N", CultureInfo.InvariantCulture));
        File.WriteAllBytes(caminho, MontarZip());
        return caminho;
    }

    private void AdicionarEntrada(ZipArchive zip, string tabela)
    {
        List<string> linhas = ArquivoSqlDneSintetico.ComFechamentoEmDuasLinhas(tabela, Versao);
        bool comExtra = _colunaExtraPorTabela.TryGetValue(tabela, out string? extra);
        if (comExtra)
        {
            linhas.Insert(linhas.Count - 2, ArquivoSqlDneSintetico.Coluna(extra!, TipoDeColunaDne.Texto));
        }

        linhas.AddRange(_insertsPorTabela[tabela].Select(linha => ArquivoSqlDneSintetico.Insert(Valores(linha, comExtra), tabela, Versao)));

        ZipArchiveEntry entrada = zip.CreateEntry(NomeDaTabelaDne.Entrada(Versao, tabela));
        using Stream conteudo = entrada.Open();
        using Stream sql = ArquivoSqlDneSintetico.Fluxo(linhas);
        sql.CopyTo(conteudo);
    }

    // Linhas montadas por Com() ganham o valor da coluna extra, se a tabela a tem; as brutas ficam como estão.
    private static string Valores(LinhaDoPacote linha, bool comExtra) =>
        linha.Montada && comExtra ? $"{linha.Valores}, {ValorDaColunaExtra}" : linha.Valores;

    private static string Literal((string Coluna, object? Valor)[] valores, string coluna)
    {
        object? valor = valores.FirstOrDefault(v => v.Coluna == coluna).Valor;
        return valor switch
        {
            null => "NULL",
            int inteiro => inteiro.ToString(CultureInfo.InvariantCulture),
            string texto => "'" + texto.Replace("'", "''", StringComparison.Ordinal) + "'",
            _ => throw new ArgumentException("Valor fora dos tipos do pacote.", nameof(valores)),
        };
    }

    private sealed record LinhaDoPacote(string Valores, bool Montada);
}
