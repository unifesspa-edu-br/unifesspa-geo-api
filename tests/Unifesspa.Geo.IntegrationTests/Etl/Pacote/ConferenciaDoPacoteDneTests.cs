namespace Unifesspa.Geo.IntegrationTests.Etl.Pacote;

using AwesomeAssertions;

using Unifesspa.Geo.Domain.Errors;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;
using Unifesspa.Geo.Kernel.Results;

/// <summary>Conferência do diretório do zip: lista fechada, limites e versão única.</summary>
public sealed class ConferenciaDoPacoteDneTests
{
    private const string NomeSigiloso = "MARCADOR-SIGILOSO-FICTICIO-7731.txt";

    [Fact(DisplayName = "As 15 tabelas da mesma versão são aceitas e a versão sai dos nomes")]
    public void QuinzeTabelas_SaoAceitas()
    {
        Result<string> resultado = ConferenciaDoPacoteDne.Conferir(EntradasSinteticasDoPacote.Completas("202610"));

        resultado.IsSuccess.Should().BeTrue();
        resultado.Value.Should().Be("202610");
    }

    [Fact(DisplayName = "A imagem 0relacionamentos.jpg é aceita e ignorada")]
    public void ImagemAuxiliar_EhAceita()
    {
        List<EntradaDoPacoteDne> entradas = EntradasSinteticasDoPacote.Completas();
        entradas.Add(EntradasSinteticasDoPacote.Imagem());

        ConferenciaDoPacoteDne.Conferir(entradas).IsSuccess.Should().BeTrue();
    }

    [Fact(DisplayName = "Tabela faltando e tabela repetida são citadas pelo nome")]
    public void TabelaFaltandoERepetida_SaoCitadas()
    {
        List<EntradaDoPacoteDne> entradas = EntradasSinteticasDoPacote.Completas();
        entradas.RemoveAll(e => e.Nome.EndsWith("_n_estado.sql", StringComparison.Ordinal));
        entradas.Add(EntradasSinteticasDoPacote.Sql("bairro", ArquivoSqlDneSintetico.Versao));

        string detalhe = Falha(entradas);

        detalhe.Should().Contain("ausentes: estado").And.Contain("repetidas: bairro");
    }

    [Fact(DisplayName = "Entradas desconhecidas e imagem repetida são recusadas sem citar o nome do arquivo")]
    public void EntradaDesconhecida_EhRecusadaSemCitarONome()
    {
        List<EntradaDoPacoteDne> entradas = EntradasSinteticasDoPacote.Completas();
        entradas.AddRange([new EntradaDoPacoteDne(NomeSigiloso, 10, false), EntradasSinteticasDoPacote.Imagem(), EntradasSinteticasDoPacote.Imagem()]);

        string detalhe = Falha(entradas);

        detalhe.Should().Contain("1 entrada(s) que não pertencem").And.Contain("mais de uma vez").And.NotContain(NomeSigiloso);
    }

    [Theory(DisplayName = "Nome com subpasta, barra invertida, '..' ou acima de 128 caracteres é recusado")]
    [InlineData("pasta/tbl_cep_202601_n_estado.sql")]
    [InlineData("pasta\\tbl_cep_202601_n_estado.sql")]
    [InlineData("../tbl_cep_202601_n_estado.sql")]
    [InlineData("tbl_cep_202601_n_estado..sql")]
    [InlineData("pasta/")]
    public void NomeInseguro_EhRecusado(string nome)
    {
        List<EntradaDoPacoteDne> entradas = EntradasSinteticasDoPacote.Completas();
        entradas.Add(new EntradaDoPacoteDne(nome, 10, false));

        Falha(entradas).Should().Contain("nome inválido").And.NotContain(nome);
    }

    [Fact(DisplayName = "Nome acima de 128 caracteres é recusado")]
    public void NomeLongo_EhRecusado()
    {
        List<EntradaDoPacoteDne> entradas = EntradasSinteticasDoPacote.Completas();
        entradas.Add(new EntradaDoPacoteDne(new string('a', LimitesDoPacoteDne.CaracteresNoNomeDaEntrada + 1), 10, false));

        Falha(entradas).Should().Contain("nome inválido");
    }

    [Fact(DisplayName = "Mais de 16 entradas são recusadas")]
    public void MaisDeDezesseisEntradas_SaoRecusadas()
    {
        List<EntradaDoPacoteDne> entradas = EntradasSinteticasDoPacote.Completas();
        entradas.AddRange([EntradasSinteticasDoPacote.Imagem(), EntradasSinteticasDoPacote.Imagem()]);

        Falha(entradas).Should().Contain("mais de 16 entradas");
    }

    [Fact(DisplayName = "Entrada cifrada é recusada")]
    public void EntradaCifrada_EhRecusada()
    {
        List<EntradaDoPacoteDne> entradas = EntradasSinteticasDoPacote.Completas();
        entradas[0] = entradas[0] with { Cifrada = true };

        Falha(entradas).Should().Contain("1 entrada(s) cifrada(s)");
    }

    [Fact(DisplayName = "Tamanho declarado acima de 768 MiB por entrada e de 1 GiB no total é recusado")]
    public void TamanhosDeclarados_SaoLimitados()
    {
        List<EntradaDoPacoteDne> grande = EntradasSinteticasDoPacote.Completas();
        grande[0] = grande[0] with { TamanhoDeclarado = LimitesDoPacoteDne.BytesPorEntrada + 1 };
        List<EntradaDoPacoteDne> somaAlta = [.. EntradasSinteticasDoPacote.Completas().Select(e => e with { TamanhoDeclarado = LimitesDoPacoteDne.BytesPorEntrada / 8 })];

        Falha(grande).Should().Contain("acima de 768 MiB");
        Falha(somaAlta).Should().Contain("passa de 1 GiB");
    }

    [Fact(DisplayName = "Tamanhos no limite exato são aceitos")]
    public void TamanhosNoLimite_SaoAceitos()
    {
        List<EntradaDoPacoteDne> entradas = EntradasSinteticasDoPacote.Completas();
        entradas[0] = entradas[0] with { TamanhoDeclarado = LimitesDoPacoteDne.BytesPorEntrada };

        ConferenciaDoPacoteDne.Conferir(entradas).IsSuccess.Should().BeTrue();
    }

    [Fact(DisplayName = "Versões misturadas entre as tabelas são recusadas com as versões citadas")]
    public void VersoesMisturadas_SaoRecusadas()
    {
        List<EntradaDoPacoteDne> entradas = EntradasSinteticasDoPacote.Completas("202609");
        entradas[3] = EntradasSinteticasDoPacote.Sql(TabelasDne.Nomes[3], "202610");

        Falha(entradas).Should().Contain("versões diferentes: 202609, 202610");
    }

    [Theory(DisplayName = "Versão com mês fora de 01 a 12 é recusada")]
    [InlineData("202600")]
    [InlineData("202613")]
    public void MesInvalido_EhRecusado(string versao)
    {
        Falha(EntradasSinteticasDoPacote.Completas(versao)).Should().Contain("mês deve estar entre 01 e 12");
    }

    [Fact(DisplayName = "Todas as faltas voltam juntas na mesma mensagem, com o código do pacote inválido")]
    public void FaltasAcumuladas_VoltamJuntas()
    {
        List<EntradaDoPacoteDne> entradas = [.. EntradasSinteticasDoPacote.Completas().Skip(2), new EntradaDoPacoteDne("../x", 1, true)];

        Result<string> resultado = ConferenciaDoPacoteDne.Conferir(entradas);

        resultado.Error!.Code.Should().Be(GeoImportacaoErrorCodes.PacoteInvalido);
        resultado.Error.Message.Should().Contain("nome inválido").And.Contain("cifrada").And.Contain($"ausentes: {string.Join(", ", TabelasDne.Nomes.Take(2))}");
    }

    private static string Falha(List<EntradaDoPacoteDne> entradas)
    {
        Result<string> resultado = ConferenciaDoPacoteDne.Conferir(entradas);

        resultado.IsFailure.Should().BeTrue();
        return resultado.Error!.Message;
    }
}
