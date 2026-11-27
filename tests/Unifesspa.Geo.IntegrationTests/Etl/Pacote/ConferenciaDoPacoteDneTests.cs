namespace Unifesspa.Geo.IntegrationTests.Etl.Pacote;

using AwesomeAssertions;

using Unifesspa.Geo.Domain.Errors;
using Unifesspa.Geo.Infrastructure.Persistence.Etl;
using Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;
using Unifesspa.Geo.Kernel.Results;

/// <summary>Conferência do diretório do zip: lista fechada, limites e versão única.</summary>
public sealed class ConferenciaDoPacoteDneTests
{
    private const string NomeSigiloso = "MARCADOR-SIGILOSO-FICTICIO-7731.sql";
    private const int Limite = EtlOpcoes.QuantidadePadraoDeEntradasDoPacote;

    [Fact(DisplayName = "As 15 tabelas da mesma versão são aceitas e a versão sai dos nomes")]
    public void QuinzeTabelas_SaoAceitas()
    {
        Result<string> resultado = ConferenciaDoPacoteDne.Conferir(EntradasSinteticasDoPacote.Completas("202610"), Limite);

        resultado.IsSuccess.Should().BeTrue();
        resultado.Value.Should().Be("202610");
    }

    [Theory(DisplayName = "Arquivos que acompanham o pacote (jpg, png, outros) são aceitos e ignorados, em qualquer quantidade")]
    [InlineData("0relacionamentos.jpg")]
    [InlineData("0relacionamentos.png")]
    [InlineData("LEIAME")]
    public void ArquivoQueAcompanhaOPacote_EhAceitoEIgnorado(string nome)
    {
        List<EntradaDoPacoteDne> entradas = EntradasSinteticasDoPacote.Completas();
        entradas.AddRange([EntradasSinteticasDoPacote.Imagem(nome), EntradasSinteticasDoPacote.Imagem(nome)]);

        ConferenciaDoPacoteDne.Conferir(entradas, Limite).IsSuccess.Should().BeTrue();
    }

    [Fact(DisplayName = "Formato de 202610: entrada da pasta, imagem png e as tabelas dentro de uma pasta, com nome qualquer")]
    public void FormatoComPasta_EhAceito()
    {
        Result<string> resultado = ConferenciaDoPacoteDne.Conferir(EntradasSinteticasDoPacote.CompletasNaPasta("qualquer-Pasta_9", "202610"), Limite);

        resultado.IsSuccess.Should().BeTrue();
        resultado.Value.Should().Be("202610");
    }

    [Fact(DisplayName = "Pasta sem a entrada da própria pasta é aceita")]
    public void PastaSemEntradaDePasta_EhAceita()
    {
        List<EntradaDoPacoteDne> entradas = EntradasSinteticasDoPacote.CompletasNaPasta();
        entradas.RemoveAt(0);

        ConferenciaDoPacoteDne.Conferir(entradas, Limite).IsSuccess.Should().BeTrue();
    }

    [Fact(DisplayName = "Mistura de entradas no topo e na pasta é recusada")]
    public void MisturaDeTopoEPasta_EhRecusada()
    {
        List<EntradaDoPacoteDne> entradas = EntradasSinteticasDoPacote.CompletasNaPasta();
        entradas[2] = entradas[2] with { Nome = entradas[2].Nome[(EntradasSinteticasDoPacote.PastaDoPacote.Length + 1)..] };

        Falha(entradas).Should().Contain("As entradas não estão todas na mesma pasta.");
    }

    [Fact(DisplayName = "Duas pastas diferentes são recusadas")]
    public void DuasPastas_SaoRecusadas()
    {
        List<EntradaDoPacoteDne> entradas = EntradasSinteticasDoPacote.CompletasNaPasta();
        entradas[2] = entradas[2] with { Nome = "outra/" + entradas[2].Nome[(EntradasSinteticasDoPacote.PastaDoPacote.Length + 1)..] };

        Falha(entradas).Should().Contain("mesma pasta");
    }

    [Fact(DisplayName = "Entrada de pasta de outra pasta é recusada")]
    public void EntradaDePastaDeOutraPasta_EhRecusada()
    {
        List<EntradaDoPacoteDne> entradas = EntradasSinteticasDoPacote.CompletasNaPasta();
        entradas[0] = EntradasSinteticasDoPacote.Pasta("outra");

        Falha(entradas).Should().Contain("mesma pasta");
    }

    [Fact(DisplayName = "Entrada de pasta repetida ou com tamanho diferente de zero é recusada")]
    public void EntradaDePastaMalFormada_EhRecusada()
    {
        List<EntradaDoPacoteDne> repetida = EntradasSinteticasDoPacote.CompletasNaPasta();
        repetida.Add(EntradasSinteticasDoPacote.Pasta(EntradasSinteticasDoPacote.PastaDoPacote));
        List<EntradaDoPacoteDne> comTamanho = EntradasSinteticasDoPacote.CompletasNaPasta();
        comTamanho[0] = EntradasSinteticasDoPacote.Pasta(EntradasSinteticasDoPacote.PastaDoPacote, tamanho: 1);

        Falha(repetida).Should().Contain("1 entrada(s) com nome inválido");
        Falha(comTamanho).Should().Contain("1 entrada(s) com nome inválido");
    }

    [Theory(DisplayName = "Dois níveis de pasta, pasta com caractere fora da faixa ou acima de 64 caracteres é recusada")]
    [InlineData("a/b/tbl_cep_202601_n_estado.sql")]
    [InlineData("a/b/")]
    [InlineData("pasta com espaço/tbl_cep_202601_n_estado.sql")]
    [InlineData("pásta/tbl_cep_202601_n_estado.sql")]
    [InlineData("pasta.ponto/tbl_cep_202601_n_estado.sql")]
    [InlineData("/tbl_cep_202601_n_estado.sql")]
    [InlineData("c:/tbl_cep_202601_n_estado.sql")]
    public void PastaForaDoPadrao_EhRecusada(string nome)
    {
        List<EntradaDoPacoteDne> entradas = EntradasSinteticasDoPacote.Completas();
        entradas.Add(new EntradaDoPacoteDne(nome, 0, false));

        Falha(entradas).Should().Contain("nome inválido").And.NotContain(nome);
    }

    [Fact(DisplayName = "Pasta de 64 caracteres é aceita e de 65 é recusada")]
    public void TamanhoDaPasta_EhLimitado()
    {
        string no64 = new('a', 64);
        string no65 = new('a', 65);

        ConferenciaDoPacoteDne.Conferir(EntradasSinteticasDoPacote.CompletasNaPasta(no64), Limite).IsSuccess.Should().BeTrue();
        Falha(EntradasSinteticasDoPacote.CompletasNaPasta(no65)).Should().Contain("nome inválido");
    }

    [Theory(DisplayName = "Arquivo .sql desconhecido é recusado como fora do pacote, em qualquer caixa")]
    [InlineData("outra_tabela.sql")]
    [InlineData("tbl_cep_202601_n_estado.SQL")]
    [InlineData("tbl_cep_202601_n_tabela_desconhecida.sql")]
    public void SqlDesconhecido_EhRecusado(string nome)
    {
        List<EntradaDoPacoteDne> entradas = EntradasSinteticasDoPacote.Completas();
        entradas.Add(new EntradaDoPacoteDne(nome, 10, false));

        Falha(entradas).Should().Contain("1 entrada(s) que não pertencem").And.NotContain(nome);
    }

    [Fact(DisplayName = "Arquivo que acompanha o pacote acima de 5 MiB é recusado e no limite é aceito")]
    public void ArquivoQueAcompanhaOPacote_EhLimitado()
    {
        List<EntradaDoPacoteDne> noLimite = EntradasSinteticasDoPacote.Completas();
        noLimite.Add(EntradasSinteticasDoPacote.Imagem(tamanho: LimitesDoPacoteDne.BytesPorArquivoQueAcompanhaOPacote));
        List<EntradaDoPacoteDne> acima = EntradasSinteticasDoPacote.Completas();
        acima.Add(EntradasSinteticasDoPacote.Imagem(tamanho: LimitesDoPacoteDne.BytesPorArquivoQueAcompanhaOPacote + 1));

        ConferenciaDoPacoteDne.Conferir(noLimite, Limite).IsSuccess.Should().BeTrue();
        Falha(acima).Should().Contain("acima de 5 MiB");
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

    [Fact(DisplayName = "Entrada .sql desconhecida é recusada sem citar o nome do arquivo")]
    public void EntradaDesconhecida_EhRecusadaSemCitarONome()
    {
        List<EntradaDoPacoteDne> entradas = EntradasSinteticasDoPacote.Completas();
        entradas.Add(new EntradaDoPacoteDne(NomeSigiloso, 10, false));

        string detalhe = Falha(entradas);

        detalhe.Should().Contain("1 entrada(s) que não pertencem").And.NotContain(NomeSigiloso);
    }

    [Theory(DisplayName = "Nome com barra invertida, '..' ou ':' é recusado")]
    [InlineData("pasta\\tbl_cep_202601_n_estado.sql")]
    [InlineData("../tbl_cep_202601_n_estado.sql")]
    [InlineData("tbl_cep_202601_n_estado..sql")]
    [InlineData("pasta:tbl_cep_202601_n_estado.sql")]
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

    [Fact(DisplayName = "Entradas acima do limite configurado são recusadas, contando também a entrada de pasta, e o valor sai na mensagem")]
    public void EntradasAcimaDoLimiteConfigurado_SaoRecusadas()
    {
        List<EntradaDoPacoteDne> noLimite = EntradasSinteticasDoPacote.CompletasNaPasta();
        List<EntradaDoPacoteDne> acima = EntradasSinteticasDoPacote.CompletasNaPasta();
        acima.Add(EntradasSinteticasDoPacote.Imagem($"{EntradasSinteticasDoPacote.PastaDoPacote}/extra.png"));

        ConferenciaDoPacoteDne.Conferir(noLimite, noLimite.Count).IsSuccess.Should().BeTrue();
        ConferenciaDoPacoteDne.Conferir(acima, noLimite.Count).Error!.Message.Should().Contain($"mais de {noLimite.Count} entradas");
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

        ConferenciaDoPacoteDne.Conferir(entradas, Limite).IsSuccess.Should().BeTrue();
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

        Result<string> resultado = ConferenciaDoPacoteDne.Conferir(entradas, Limite);

        resultado.Error!.Code.Should().Be(GeoImportacaoErrorCodes.PacoteInvalido);
        resultado.Error.Message.Should().Contain("nome inválido").And.Contain("cifrada").And.Contain($"ausentes: {string.Join(", ", TabelasDne.Nomes.Take(2))}");
    }

    private static string Falha(List<EntradaDoPacoteDne> entradas)
    {
        Result<string> resultado = ConferenciaDoPacoteDne.Conferir(entradas, Limite);

        resultado.IsFailure.Should().BeTrue();
        return resultado.Error!.Message;
    }
}
