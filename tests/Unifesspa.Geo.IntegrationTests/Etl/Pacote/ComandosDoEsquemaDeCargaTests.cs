namespace Unifesspa.Geo.IntegrationTests.Etl.Pacote;

using AwesomeAssertions;

using Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

/// <summary>Nomes do esquema de carga e comandos SQL gerados só com identificadores conferidos e tipos da lista fechada.</summary>
public sealed class ComandosDoEsquemaDeCargaTests
{
    private const string Esquema = "carga_dne_0123456789abcdef0123456789abcdef";

    [Fact(DisplayName = "O esquema da execução chama carga_dne_ seguido do identificador sem hífens")]
    public void Nome_UsaOIdentificadorSemHifens()
    {
        Guid execucao = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");

        ComandosDoEsquemaDeCarga.Nome(execucao).Should().Be(Esquema);
        ComandosDoEsquemaDeCarga.Nome(execucao).Should().HaveLength(42);
    }

    [Theory(DisplayName = "Só o nome exato produzido para uma execução é aceito como esquema de carga")]
    [InlineData("carga_dne_0123456789abcdef0123456789abcdef", true)]
    [InlineData("carga_dne_0123456789ABCDEF0123456789abcdef", false)]
    [InlineData("carga_dne_0123456789abcdef0123456789abcde", false)]
    [InlineData("carga_dne_0123456789abcdef0123456789abcdef0", false)]
    [InlineData("carga_dne_0123456789abcdef0123456789abcdeg", false)]
    [InlineData("carga_dne_outro", false)]
    [InlineData("public", false)]
    [InlineData("", false)]
    public void EhNomeValido_ConfereAForma(string nome, bool esperado) =>
        ComandosDoEsquemaDeCarga.EhNomeValido(nome).Should().Be(esperado);

    [Fact(DisplayName = "Toda tabela do pacote cabe no esquema de carga com nome de até 22 caracteres")]
    public void NomesDasTabelas_TemAte22Caracteres() =>
        TabelasDne.Nomes.Should().OnlyContain(nome => nome.Length <= 22);

    [Fact(DisplayName = "A tabela é criada UNLOGGED com text para varchar e integer para int4")]
    public void CriarTabela_GeraTabelaSemRegistroDeTransacaoComTiposFechados()
    {
        ComandoDoEsquemaDeCarga comando = ComandosDoEsquemaDeCarga.CriarTabela(Esquema, "cidade_faixa");

        comando.Texto.Should().Be(
            $"CREATE UNLOGGED TABLE \"{Esquema}\".\"cidade_faixa\" (\"faixa_ini\" text, \"faixa_fim\" text, \"id_cidade\" integer)");
    }

    [Fact(DisplayName = "O COPY lista as colunas na ordem do arquivo e usa o formato binário")]
    public void Copiar_ListaAsColunasEmFormatoBinario()
    {
        ComandoDoEsquemaDeCarga comando = ComandosDoEsquemaDeCarga.Copiar(Esquema, "cidade_faixa");

        comando.Texto.Should().Be(
            $"COPY \"{Esquema}\".\"cidade_faixa\" (\"faixa_ini\", \"faixa_fim\", \"id_cidade\") FROM STDIN (FORMAT BINARY)");
    }

    [Fact(DisplayName = "O descarte do esquema usa DROP SCHEMA IF EXISTS ... CASCADE")]
    public void DescartarEsquema_RemoveComDependencias() =>
        ComandosDoEsquemaDeCarga.DescartarEsquema(Esquema).Texto.Should().Be($"DROP SCHEMA IF EXISTS \"{Esquema}\" CASCADE");

    [Fact(DisplayName = "Tabela fora da lista do pacote é recusada")]
    public void CriarTabela_ForaDaLista_Lanca()
    {
        Action criar = () => ComandosDoEsquemaDeCarga.CriarTabela(Esquema, "usuarios");

        criar.Should().Throw<ArgumentException>();
    }

    [Fact(DisplayName = "A consulta lista só as colunas pedidas, qualificadas pelo esquema e pela tabela")]
    public void Consultar_ListaAsColunasPedidas()
    {
        ComandoDoEsquemaDeCarga comando = ComandosDoEsquemaDeCarga.Consultar(
            Esquema, "cidade_faixa", FiltroDeLinhasDoEsquema.Nenhum, "id_cidade", "faixa_ini");

        comando.Texto.Should().Be($"SELECT \"id_cidade\", \"faixa_ini\" FROM \"{Esquema}\".\"cidade_faixa\"");
    }

    [Theory(DisplayName = "A consulta de logradouros aplica o filtro fixo escolhido")]
    [InlineData(true, " WHERE btrim(")]
    [InlineData(false, " WHERE NOT (btrim(")]
    public void Consultar_AplicaOFiltro(bool semNome, string trechoEsperado)
    {
        FiltroDeLinhasDoEsquema filtro = semNome
            ? FiltroDeLinhasDoEsquema.SemNomeDeLogradouro
            : FiltroDeLinhasDoEsquema.ComNomeDeLogradouro;

        ComandosDoEsquemaDeCarga.Consultar(Esquema, "logradouro", filtro, "cep").Texto.Should().Contain(trechoEsperado);
    }

    [Theory(DisplayName = "Coluna fora das obrigatórias da tabela, ou com texto fora do padrão, é recusada na consulta")]
    [InlineData("cidade_sem_acento")]
    [InlineData("faixa_ini\"; DROP TABLE x; --")]
    [InlineData("FAIXA_INI")]
    [InlineData("")]
    public void Consultar_ComColunaForaDasObrigatorias_Lanca(string coluna)
    {
        Action consultar = () => ComandosDoEsquemaDeCarga.Consultar(
            Esquema, "cidade_faixa", FiltroDeLinhasDoEsquema.Nenhum, coluna);

        consultar.Should().Throw<ArgumentException>();
    }

    [Fact(DisplayName = "As colunas criadas e copiadas de toda tabela são exatamente as obrigatórias, e nenhuma é do arquivo")]
    public void TodasAsTabelas_UsamSoAsColunasObrigatorias()
    {
        foreach (string tabela in TabelasDne.Nomes)
        {
            string[] nomes = [.. TabelasDne.ColunasObrigatorias(tabela).Select(c => $"\"{c.Nome}\"")];

            ComandosDoEsquemaDeCarga.CriarTabela(Esquema, tabela).Texto.Should().ContainAll(nomes);
            ComandosDoEsquemaDeCarga.Copiar(Esquema, tabela).Texto.Should().Contain($"({string.Join(", ", nomes)})");
        }
    }

    [Fact(DisplayName = "Esquema fora do padrão é recusado ao montar qualquer comando")]
    public void Comandos_ComEsquemaForaDoPadrao_Lancam()
    {
        Action criar = () => ComandosDoEsquemaDeCarga.CriarEsquema("x\"; DROP SCHEMA public; --");

        criar.Should().Throw<ArgumentException>();
    }
}
