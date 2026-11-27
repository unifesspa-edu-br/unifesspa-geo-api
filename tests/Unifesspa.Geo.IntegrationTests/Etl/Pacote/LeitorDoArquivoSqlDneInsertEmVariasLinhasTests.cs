namespace Unifesspa.Geo.IntegrationTests.Etl.Pacote;

using System.Text;

using AwesomeAssertions;

using Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

/// <summary>
/// O INSERT pode cruzar linhas só dentro de texto entre aspas simples, com os mesmos bytes do arquivo,
/// até 8 linhas e 64 KiB. Fora disso a gramática continua fechada e as recusas citam a linha de início.
/// </summary>
public sealed class LeitorDoArquivoSqlDneInsertEmVariasLinhasTests
{
    private static readonly string Inicio =
        $"INSERT INTO \"public\".\"{ArquivoSqlDneSintetico.Nome(ArquivoSqlDneSintetico.Tabela, ArquivoSqlDneSintetico.Versao)}\" VALUES (";

    private static readonly int LinhasDoCabecalho = ArquivoSqlDneSintetico.ComFechamentoEmUmaLinha().Count;

    private static int PrimeiraLinhaDoCorpo => LinhasDoCabecalho + 1;

    [Theory(DisplayName = "A quebra dentro do texto fica no valor com os mesmos bytes: LF, CRLF e CR solto")]
    [InlineData("a\nb")]
    [InlineData("a\r\nb")]
    [InlineData("a\rb")]
    public async Task QuebraDentroDoTexto_FicaNoValor(string texto)
    {
        List<object?[]> lidas = await LerAsync($"{Inicio}'{texto}', 'x', 1);\r\n");

        lidas.Should().ContainSingle().Which.Should().Equal(texto, "x", 1);
    }

    [Fact(DisplayName = "Arquivo só com LF: a quebra dentro do texto continua sendo LF")]
    public async Task ArquivoComLf_GuardaLf()
    {
        List<object?[]> lidas = await LerAsync($"{Inicio}'a\nb', 'x', 1);\n", fimDeLinhaDoCabecalho: "\n");

        lidas.Should().ContainSingle().Which[0].Should().Be("a\nb");
    }

    [Fact(DisplayName = "Aspa dobrada junto à quebra vale uma aspa e a quebra")]
    public async Task AspaDobradaJuntoDaQuebra_VaiParaOValor()
    {
        List<object?[]> lidas = await LerAsync($"{Inicio}'ab''\nx', 'y', 1);\r\n");

        lidas.Should().ContainSingle().Which.Should().Equal("ab'\nx", "y", 1);
    }

    [Fact(DisplayName = "Caso real: quebra no fim do valor e segunda linha começando por aspa e vírgula")]
    public async Task QuebraNoFimDoValor_SegundaLinhaComecaPorAspa()
    {
        List<object?[]> lidas = await LerAsync($"{Inicio}'Nome\n', 'x', 1);\r\n{Inicio}'b', 'c', 2);\r\n");

        lidas.Should().HaveCount(2);
        lidas[0].Should().Equal("Nome\n", "x", 1);
        lidas[1].Should().Equal("b", "c", 2);
    }

    [Fact(DisplayName = "Texto que fecha exatamente na oitava linha é aceito")]
    public async Task TextoNaOitavaLinha_EhAceito()
    {
        List<object?[]> lidas = await LerAsync($"{Inicio}'a\r\n2\r\n3\r\n4\r\n5\r\n6\r\n7\r\nz', 'x', 1);\r\n");

        lidas.Should().ContainSingle();
    }

    [Fact(DisplayName = "Aspa que não fecha em 8 linhas é recusada com a linha de início")]
    public async Task AspaSemFecharEmOitoLinhas_EhRecusada()
    {
        ErroDeLeituraDoPacoteDneException erro = await ErroAsync($"{Inicio}'a\r\nb\r\nc\r\nd\r\ne\r\nf\r\ng\r\nh\r\ni\r\n");

        erro.Linha.Should().Be(PrimeiraLinhaDoCorpo);
        erro.Message.Should().Contain("não fecha em até 8 linhas");
    }

    [Fact(DisplayName = "Linha acima de 64 KiB dentro do texto é recusada na linha física")]
    public async Task LinhaGrandeDentroDoTexto_EhRecusada()
    {
        ErroDeLeituraDoPacoteDneException erro = await ErroAsync($"{Inicio}'a\r\n{new string('b', LimitesDoPacoteDne.BytesPorLinha + 1)}\r\n");

        erro.Linha.Should().Be(PrimeiraLinhaDoCorpo + 1);
        erro.Message.Should().Contain("a linha ultrapassa");
    }

    [Fact(DisplayName = "Comando acima de 64 KiB somando linhas menores é recusado com a linha de início")]
    public async Task ComandoGrandeEmLinhasMenores_EhRecusado()
    {
        string bloco = new string('b', 20_000) + "\r\n";

        ErroDeLeituraDoPacoteDneException erro = await ErroAsync($"{Inicio}'a\r\n{bloco}{bloco}{bloco}{bloco}");

        erro.Linha.Should().Be(PrimeiraLinhaDoCorpo);
        erro.Message.Should().Contain("o comando ultrapassa o limite de 64 KiB");
    }

    [Fact(DisplayName = "Fim do arquivo dentro do texto é recusado com a linha de início")]
    public async Task FimDoArquivoDentroDoTexto_EhRecusado()
    {
        ErroDeLeituraDoPacoteDneException erro = await ErroAsync($"{Inicio}'a\r\nb");

        erro.Linha.Should().Be(PrimeiraLinhaDoCorpo);
        erro.Message.Should().Contain("termina antes");
    }

    [Fact(DisplayName = "Depois de um INSERT de duas linhas, o erro do comando seguinte cita a linha física certa")]
    public async Task ErroDepoisDeInsertDeDuasLinhas_CitaALinhaFisica()
    {
        ErroDeLeituraDoPacoteDneException erro = await ErroAsync($"{Inicio}'a\r\nb', 'x', 1);\r\n{Inicio}E'y', 'x', 1);\r\n");

        erro.Linha.Should().Be(PrimeiraLinhaDoCorpo + 2);
    }

    [Fact(DisplayName = "Erro de valor num INSERT de várias linhas cita a linha de início")]
    public async Task ErroDeValorEmInsertDeVariasLinhas_CitaALinhaDeInicio()
    {
        ErroDeLeituraDoPacoteDneException erro = await ErroAsync($"{Inicio}'a\r\nb', 'x', 1.5);\r\n");

        erro.Linha.Should().Be(PrimeiraLinhaDoCorpo);
        erro.Message.Should().Contain("valores do INSERT");
    }

    [Theory(DisplayName = "Quebra fora das aspas, E'..' e aspa escapada continuam recusados")]
    [InlineData("'a',\r\n'x', 1);\r\n")]
    [InlineData("'a'\r\n'b', 'x', 1);\r\n")]
    [InlineData("E'ab', 'x', 1);\r\n")]
    [InlineData("E'a\\'b', 'x', 1);\r\n")]
    [InlineData("'a', 'x', 1)\r\n;\r\n")]
    public async Task FormasForaDaGramatica_ContinuamRecusadas(string valores)
    {
        await ErroAsync($"{Inicio}{valores}");
    }

    [Fact(DisplayName = "Aspa sem par em comentário não abre texto")]
    public async Task AspaEmComentario_NaoAbreTexto()
    {
        List<object?[]> lidas = await LerAsync($"-- don't\r\n{Inicio}'a', 'x', 1);\r\n");

        lidas.Should().ContainSingle();
    }

    private static async Task<List<object?[]>> LerAsync(string corpo, string fimDeLinhaDoCabecalho = "\r\n")
    {
        using MemoryStream fluxo = Montar(corpo, fimDeLinhaDoCabecalho);
        return await LeituraDoArquivoSqlDne.LerTudoAsync(fluxo);
    }

    private static async Task<ErroDeLeituraDoPacoteDneException> ErroAsync(string corpo)
    {
        Func<Task> acao = async () =>
        {
            using MemoryStream fluxo = Montar(corpo, "\r\n");
            await LeituraDoArquivoSqlDne.LerTudoAsync(fluxo);
        };
        ErroDeLeituraDoPacoteDneException erro = (await acao.Should().ThrowAsync<ErroDeLeituraDoPacoteDneException>()).Which;
        erro.Tabela.Should().Be(ArquivoSqlDneSintetico.Tabela);
        return erro;
    }

    private static MemoryStream Montar(string corpo, string fimDeLinhaDoCabecalho)
    {
        string cabecalho = string.Join(fimDeLinhaDoCabecalho, ArquivoSqlDneSintetico.ComFechamentoEmUmaLinha()) + fimDeLinhaDoCabecalho;
        return new MemoryStream(new UTF8Encoding(false).GetBytes(cabecalho + corpo));
    }
}
