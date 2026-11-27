namespace Unifesspa.Geo.IntegrationTests.Etl.Pacote;

using System.Text;

using AwesomeAssertions;

using Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

/// <summary>Cada comando da gramática fechada do arquivo SQL do pacote é aceito e lido sem execução.</summary>
public sealed class LeitorDoArquivoSqlDneComandosAceitosTests
{
    private static readonly string Nome = ArquivoSqlDneSintetico.Nome(ArquivoSqlDneSintetico.Tabela, ArquivoSqlDneSintetico.Versao);

    [Fact(DisplayName = "CREATE TABLE fechado em uma linha com ');' é aceito")]
    public async Task FechamentoEmUmaLinha_EhAceito()
    {
        List<string> linhas = ArquivoSqlDneSintetico.ComFechamentoEmUmaLinha();
        linhas.Add(ArquivoSqlDneSintetico.Insert("'0100', '0199', 7"));

        List<object?[]> lidas = await LeituraDoArquivoSqlDne.LerTudoAsync(linhas);

        lidas.Should().ContainSingle().Which.Should().Equal("0100", "0199", 7);
    }

    [Fact(DisplayName = "CREATE TABLE fechado em duas linhas, ')' e depois ';', é aceito")]
    public async Task FechamentoEmDuasLinhas_EhAceito()
    {
        List<string> linhas = ArquivoSqlDneSintetico.ComFechamentoEmDuasLinhas();
        linhas.Add(ArquivoSqlDneSintetico.Insert("NULL, 'a', -3"));

        List<object?[]> lidas = await LeituraDoArquivoSqlDne.LerTudoAsync(linhas);

        lidas.Should().ContainSingle().Which.Should().Equal(new object?[] { null, "a", -3 });
    }

    [Theory(DisplayName = "BOM opcional e fim de linha CRLF ou LF são aceitos")]
    [InlineData("\r\n", true)]
    [InlineData("\n", false)]
    public async Task BomEFimDeLinha_SaoAceitos(string fimDeLinha, bool comBom)
    {
        List<string> linhas = ArquivoSqlDneSintetico.ComFechamentoEmUmaLinha();
        linhas.Add(ArquivoSqlDneSintetico.Insert(ArquivoSqlDneSintetico.ValoresCompativeis()));
        byte[] bytes = Encoding.UTF8.GetBytes(string.Join(fimDeLinha, linhas) + fimDeLinha);
        using MemoryStream fluxo = new(comBom ? [.. Encoding.UTF8.Preamble.ToArray(), .. bytes] : bytes);

        List<object?[]> lidas = await LeituraDoArquivoSqlDne.LerTudoAsync(fluxo);

        lidas.Should().ContainSingle();
    }

    [Fact(DisplayName = "Comentários de linha e de bloco, antes e depois da tabela, são ignorados")]
    public async Task Comentarios_SaoIgnorados()
    {
        List<string> linhas =
        [
            "-- cabeçalho do dump", "/*", " Navicat", "*/", "/* numa linha só */", string.Empty,
            .. ArquivoSqlDneSintetico.ComFechamentoEmUmaLinha(),
            "  -- depois da tabela", "/* depois */",
            ArquivoSqlDneSintetico.Insert(ArquivoSqlDneSintetico.ValoresCompativeis()),
        ];

        List<object?[]> lidas = await LeituraDoArquivoSqlDne.LerTudoAsync(linhas);

        lidas.Should().ContainSingle();
    }

    [Fact(DisplayName = "CREATE INDEX em várias linhas, CREATE INDEX em uma linha e chave primária são aceitos")]
    public async Task IndicesEChavePrimaria_SaoAceitos()
    {
        List<string> linhas = ArquivoSqlDneSintetico.ComFechamentoEmDuasLinhas();
        linhas.AddRange(
        [
            $"CREATE INDEX \"idx_faixa\" ON \"public\".\"{Nome}\" USING btree (",
            "  \"faixa_ini\" COLLATE \"pg_catalog\".\"default\" \"pg_catalog\".\"text_ops\" ASC NULLS LAST,",
            "  \"id_cidade\" \"pg_catalog\".\"int4_ops\" DESC",
            ");",
            $"CREATE INDEX \"idx_cidade\" ON \"public\".\"{Nome}\" USING btree (\"id_cidade\" ASC NULLS FIRST);",
            $"ALTER TABLE \"public\".\"{Nome}\" ADD CONSTRAINT \"pk_faixa\" PRIMARY KEY (\"id_cidade\", \"faixa_ini\");",
        ]);

        List<object?[]> lidas = await LeituraDoArquivoSqlDne.LerTudoAsync(linhas);

        lidas.Should().BeEmpty();
    }

    [Fact(DisplayName = "Texto com aspa dobrada e com '); DROP' é lido como dado, sem executar nada")]
    public async Task TextoComComandoDentro_EhDado()
    {
        List<string> linhas = ArquivoSqlDneSintetico.ComFechamentoEmUmaLinha();
        linhas.Add(ArquivoSqlDneSintetico.Insert("'a''); DROP TABLE y;--', 'it''s', 1"));

        List<object?[]> lidas = await LeituraDoArquivoSqlDne.LerTudoAsync(linhas);

        lidas.Should().ContainSingle().Which.Should().Equal("a'); DROP TABLE y;--", "it's", 1);
    }

    [Fact(DisplayName = "Coluna extra no dump é tolerada e descartada: só os valores das obrigatórias seguem, na ordem da tabela")]
    public async Task ColunaExtra_EhTolerada()
    {
        List<string> linhas = ArquivoSqlDneSintetico.ComFechamentoEmUmaLinha();
        linhas.Insert(2, ArquivoSqlDneSintetico.Coluna("cidade_sem_acento", TipoDeColunaDne.Texto));
        linhas.Add(ArquivoSqlDneSintetico.Insert("'extra', 'a', 'b', 1"));

        List<object?[]> lidas = await LeituraDoArquivoSqlDne.LerTudoAsync(linhas);

        lidas.Should().ContainSingle().Which.Should().Equal("a", "b", 1);
    }

    [Fact(DisplayName = "Colunas do arquivo em outra ordem saem na ordem das obrigatórias da tabela")]
    public async Task ColunasEmOutraOrdem_SaemNaOrdemDaTabela()
    {
        List<string> linhas = ArquivoSqlDneSintetico.ComFechamentoEmUmaLinha();
        (linhas[2], linhas[4]) = (linhas[4], linhas[2]);
        linhas.Add(ArquivoSqlDneSintetico.Insert("1, 'b', 'a'"));

        List<object?[]> lidas = await LeituraDoArquivoSqlDne.LerTudoAsync(linhas);

        lidas.Should().ContainSingle().Which.Should().Equal("a", "b", 1);
    }

    [Fact(DisplayName = "Texto com acento e linha de 64 KiB exatos são aceitos")]
    public async Task AcentoELinhaNoLimite_SaoAceitos()
    {
        List<string> linhas = ArquivoSqlDneSintetico.ComFechamentoEmUmaLinha();
        string inicio = ArquivoSqlDneSintetico.Insert(string.Empty)[..^2] + "'";
        const string Fim = "', 'ã', 1);";
        int sobra = LimitesDoPacoteDne.BytesPorLinha - Encoding.UTF8.GetByteCount(inicio) - Encoding.UTF8.GetByteCount(Fim);
        linhas.Add(inicio + new string('a', sobra) + Fim);

        List<object?[]> lidas = await LeituraDoArquivoSqlDne.LerTudoAsync(linhas);

        lidas.Should().ContainSingle().Which[1].Should().Be("ã");
    }
}
