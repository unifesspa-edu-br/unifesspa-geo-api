namespace Unifesspa.Geo.IntegrationTests.Etl.Pacote;

using System.Text;

using AwesomeAssertions;

using Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

/// <summary>Todo desvio da gramática fechada interrompe a leitura, citando só a tabela e a linha.</summary>
public sealed class LeitorDoArquivoSqlDneRecusasTests
{
    private const string MarcadorSigiloso = "MARCADOR-SIGILOSO-FICTICIO-7731";

    private static readonly string Nome = ArquivoSqlDneSintetico.Nome(ArquivoSqlDneSintetico.Tabela, ArquivoSqlDneSintetico.Versao);

    [Theory(DisplayName = "INSERT com valor fora da gramática é recusado")]
    [InlineData("E'abc', 'b', 1")]
    [InlineData("$$abc$$, 'b', 1")]
    [InlineData("'a'::text, 'b', 1")]
    [InlineData("now(), 'b', 1")]
    [InlineData("'a', 'b', 1.5")]
    [InlineData("'a', 'b', 2147483648")]
    [InlineData("'a', 'b', null")]
    [InlineData("'a', 'b', 1,")]
    [InlineData("'a' 'b', 1")]
    [InlineData("'a', 'b', 1); DROP TABLE x")]
    [InlineData("'sem fechar, 'b', 1")]
    public async Task ValorForaDaGramatica_EhRecusado(string valores)
    {
        List<string> linhas = ArquivoSqlDneSintetico.ComFechamentoEmUmaLinha();
        linhas.Add(ArquivoSqlDneSintetico.Insert(valores));

        await RecusaAsync(linhas, linhas.Count);
    }

    [Theory(DisplayName = "INSERT sem os valores na quantidade e no tipo das colunas é recusado")]
    [InlineData("'a', 'b'")]
    [InlineData("'a', 'b', 1, 2")]
    [InlineData("'a', 'b', 'texto em coluna inteira'")]
    [InlineData("1, 'b', 1")]
    public async Task ValoresIncompativeis_SaoRecusados(string valores)
    {
        List<string> linhas = ArquivoSqlDneSintetico.ComFechamentoEmUmaLinha();
        linhas.Add(ArquivoSqlDneSintetico.Insert(valores));

        await RecusaAsync(linhas, linhas.Count);
    }

    [Theory(DisplayName = "Linha de INSERT mal formada é recusada")]
    [InlineData("INSERT INTO \"public\".\"tbl_cep_202601_n_cidade_faixa\" VALUES ('a', 'b', 1)")]
    [InlineData("INSERT INTO \"public\".\"tbl_cep_202601_n_cidade_faixa\" VALUES ('a', 'b', 1); SELECT 1;")]
    [InlineData("INSERT INTO \"public\".\"tbl_cep_202601_n_estado\" VALUES ('a', 'b', 1);")]
    [InlineData("INSERT INTO \"public\".\"tbl_cep_202602_n_cidade_faixa\" VALUES ('a', 'b', 1);")]
    [InlineData("INSERT INTO \"public\".\"tbl_cep_202601_n_cidade_faixa\" (a) VALUES ('a', 'b', 1);")]
    [InlineData("UPDATE x SET y = 1;")]
    [InlineData("SELECT 1;")]
    public async Task LinhaMalFormada_EhRecusada(string linhaDoInsert)
    {
        List<string> linhas = ArquivoSqlDneSintetico.ComFechamentoEmUmaLinha();
        linhas.Add(linhaDoInsert);

        await RecusaAsync(linhas, linhas.Count);
    }

    [Fact(DisplayName = "Dois INSERT na mesma linha são recusados")]
    public async Task DoisComandosNaLinha_SaoRecusados()
    {
        List<string> linhas = ArquivoSqlDneSintetico.ComFechamentoEmUmaLinha();
        string insert = ArquivoSqlDneSintetico.Insert(ArquivoSqlDneSintetico.ValoresCompativeis());
        linhas.Add(insert + insert);

        await RecusaAsync(linhas, linhas.Count);
    }

    [Fact(DisplayName = "Comando fora de ordem, repetido ou antes da tabela é recusado")]
    public async Task ComandoForaDeOrdem_EhRecusado()
    {
        string insert = ArquivoSqlDneSintetico.Insert(ArquivoSqlDneSintetico.ValoresCompativeis());
        List<string> insertAntes = [insert];
        List<string> criaduasVezes = [.. ArquivoSqlDneSintetico.ComFechamentoEmUmaLinha(), .. ArquivoSqlDneSintetico.ComFechamentoEmUmaLinha()];
        List<string> apagaDuasVezes = [.. ArquivoSqlDneSintetico.ComFechamentoEmUmaLinha()];
        apagaDuasVezes.Insert(0, apagaDuasVezes[0]);

        await RecusaAsync(insertAntes, 1);
        await RecusaAsync(criaduasVezes, ArquivoSqlDneSintetico.ComFechamentoEmUmaLinha().Count + 1);
        await RecusaAsync(apagaDuasVezes, 2);
    }

    [Theory(DisplayName = "Arquivo truncado no fim é recusado")]
    [InlineData("bloco")]
    [InlineData("criacao")]
    [InlineData("pontoevirgula")]
    [InlineData("indice")]
    [InlineData("semtabela")]
    public async Task ArquivoTruncado_EhRecusado(string ondeTermina)
    {
        List<string> linhas = ondeTermina switch
        {
            "bloco" => [.. ArquivoSqlDneSintetico.ComFechamentoEmUmaLinha(), "/* sem fim"],
            "criacao" => [.. ArquivoSqlDneSintetico.ComFechamentoEmUmaLinha().SkipLast(1)],
            "pontoevirgula" => [.. ArquivoSqlDneSintetico.ComFechamentoEmDuasLinhas().SkipLast(1)],
            "indice" => [.. ArquivoSqlDneSintetico.ComFechamentoEmUmaLinha(), $"CREATE INDEX \"i\" ON \"public\".\"{Nome}\" USING btree (", "  \"id_cidade\""],
            _ => ["-- só comentário"],
        };

        await RecusaAsync(linhas, linhas.Count);
    }

    [Theory(DisplayName = "CREATE TABLE com coluna repetida, tipo desconhecido ou obrigatória ausente ou trocada é recusado")]
    [InlineData("repetida", 4)]
    [InlineData("tipo", 3)]
    [InlineData("ausente", 5)]
    [InlineData("trocada", 6)]
    public async Task CriacaoInvalida_EhRecusada(string defeito, int linhaEsperada)
    {
        List<string> linhas = ArquivoSqlDneSintetico.ComFechamentoEmUmaLinha();
        switch (defeito)
        {
            case "repetida": linhas.Insert(2, linhas[2]); break;
            case "tipo": linhas.Insert(2, "  \"x\" \"pg_catalog\".\"numeric\","); break;
            case "ausente": linhas.RemoveAt(2); break;
            default: linhas[2] = ArquivoSqlDneSintetico.Coluna("faixa_ini", TipoDeColunaDne.Inteiro); break;
        }

        await RecusaAsync(linhas, linhaEsperada);
    }

    [Fact(DisplayName = "Índice e chave primária com coluna inexistente são recusados")]
    public async Task IndiceEChaveComColunaInexistente_SaoRecusados()
    {
        List<string> indice = [.. ArquivoSqlDneSintetico.ComFechamentoEmUmaLinha(), $"CREATE INDEX \"i\" ON \"public\".\"{Nome}\" USING btree (\"nao_existe\");"];
        List<string> chave = [.. ArquivoSqlDneSintetico.ComFechamentoEmUmaLinha(), $"ALTER TABLE \"public\".\"{Nome}\" ADD CONSTRAINT \"pk\" PRIMARY KEY (\"nao_existe\");"];

        await RecusaAsync(indice, indice.Count);
        await RecusaAsync(chave, chave.Count);
    }

    [Fact(DisplayName = "Código depois do fim do comentário de bloco é recusado")]
    public async Task CodigoDepoisDoComentario_EhRecusado()
    {
        List<string> linhas = ["/* abre", "fecha */ DROP TABLE x;"];

        await RecusaAsync(linhas, 2);
    }

    [Fact(DisplayName = "Linha acima de 64 KiB é recusada")]
    public async Task LinhaAcimaDoLimite_EhRecusada()
    {
        List<string> linhas = ArquivoSqlDneSintetico.ComFechamentoEmUmaLinha();
        linhas.Add("-- " + new string('a', LimitesDoPacoteDne.BytesPorLinha));

        await RecusaAsync(linhas, linhas.Count);
    }

    [Fact(DisplayName = "Bytes inválidos em UTF-8 são recusados")]
    public async Task Utf8Invalido_EhRecusado()
    {
        byte[] bytes = [.. Encoding.UTF8.GetBytes("-- "), 0xC0, 0xAF, .. "\r\n"u8.ToArray()];
        using MemoryStream fluxo = new(bytes);

        ErroDeLeituraDoPacoteDneException erro = await ErroAsync(LeituraDoArquivoSqlDne.LerTudoAsync(fluxo));

        erro.Linha.Should().Be(1);
    }

    [Fact(DisplayName = "Arquivo que descompacta acima do limite de bytes é recusado")]
    public async Task ArquivoAcimaDoLimiteDeBytes_EhRecusado()
    {
        using MemoryStream fluxo = ArquivoSqlDneSintetico.Fluxo(ArquivoSqlDneSintetico.ComFechamentoEmUmaLinha());

        ErroDeLeituraDoPacoteDneException erro = await ErroAsync(
            LeituraDoArquivoSqlDne.LerTudoAsync(fluxo, limiteDeBytes: fluxo.Length - 1));

        erro.Tabela.Should().Be(ArquivoSqlDneSintetico.Tabela);
    }

    [Fact(DisplayName = "Valor malformado com marcador sigiloso não aparece na mensagem de erro")]
    public async Task ValorMalformado_NaoVazaOMarcador()
    {
        List<string> linhas = ArquivoSqlDneSintetico.ComFechamentoEmUmaLinha();
        linhas.Add(ArquivoSqlDneSintetico.Insert($"E'{MarcadorSigiloso}', 'b', 1"));

        ErroDeLeituraDoPacoteDneException erro = await ErroAsync(LeituraDoArquivoSqlDne.LerTudoAsync(linhas));

        erro.ToString().Should().NotContain(MarcadorSigiloso);
        erro.Message.Should().Contain(ArquivoSqlDneSintetico.Tabela).And.Contain($"linha {linhas.Count}");
    }

    [Fact(DisplayName = "Comando desconhecido com marcador sigiloso não aparece na mensagem de erro")]
    public async Task ComandoDesconhecido_NaoVazaOMarcador()
    {
        List<string> comSelect = [.. ArquivoSqlDneSintetico.ComFechamentoEmUmaLinha(), $"SELECT '{MarcadorSigiloso}';"];
        List<string> comDrop = [.. ArquivoSqlDneSintetico.ComFechamentoEmUmaLinha(), $"DROP TABLE {MarcadorSigiloso};"];

        foreach (List<string> linhas in new[] { comSelect, comDrop })
        {
            ErroDeLeituraDoPacoteDneException erro = await ErroAsync(LeituraDoArquivoSqlDne.LerTudoAsync(linhas));
            erro.ToString().Should().NotContain(MarcadorSigiloso);
        }
    }

    private static async Task RecusaAsync(List<string> linhas, int linhaEsperada)
    {
        ErroDeLeituraDoPacoteDneException erro = await ErroAsync(LeituraDoArquivoSqlDne.LerTudoAsync(linhas));

        erro.Linha.Should().Be(linhaEsperada);
        erro.Tabela.Should().Be(ArquivoSqlDneSintetico.Tabela);
    }

    private static async Task<ErroDeLeituraDoPacoteDneException> ErroAsync(Task<List<object?[]>> leitura)
    {
        Func<Task> acao = () => leitura;
        return (await acao.Should().ThrowAsync<ErroDeLeituraDoPacoteDneException>()).Which;
    }
}
