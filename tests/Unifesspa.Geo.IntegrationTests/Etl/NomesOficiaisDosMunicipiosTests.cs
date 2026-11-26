namespace Unifesspa.Geo.IntegrationTests.Etl;

using AwesomeAssertions;

using Unifesspa.Geo.Infrastructure.Persistence.Etl.NomesOficiais;

/// <summary>
/// Lista oficial de municípios do IBGE embutida no assembly: os 5.571 municípios, o nome
/// com a grafia do IBGE por código e a recusa de arquivo malformado.
/// </summary>
public sealed class NomesOficiaisDosMunicipiosTests
{
    private const string Cabecalho = "codigo_ibge\tuf\tnome";

    [Fact(DisplayName = "A lista embutida tem os 5.571 municípios do Brasil, um por código de 7 dígitos")]
    public void ListaEmbutida_TemOsMunicipiosDoBrasil()
    {
        NomesOficiaisDosMunicipios.Padrao.Quantidade.Should().Be(5571);
    }

    [Theory(DisplayName = "Nome oficial do IBGE por código, com a grafia do IBGE")]
    [InlineData("1505551", "Pau D'Arco")]
    [InlineData("1716307", "Pau D'Arco")]
    [InlineData("2207793", "Pau D'Arco do Piauí")]
    [InlineData("3545803", "Santa Bárbara d'Oeste")]
    [InlineData("4317103", "Sant'Ana do Livramento")]
    [InlineData("1502954", "Eldorado do Carajás")]
    public void TentarObter_CodigoDaLista_DevolveNomeDoIbge(string codigo, string esperado)
    {
        bool achou = NomesOficiaisDosMunicipios.Padrao.TentarObter(codigo, out string? nome);

        achou.Should().BeTrue();
        nome.Should().Be(esperado);
    }

    [Fact(DisplayName = "Pau D'Arco do Pará e do Tocantins são municípios distintos, com o mesmo nome")]
    public void PauDArco_ExisteNosDoisEstadosComCodigosDiferentes()
    {
        NomesOficiaisDosMunicipios.Padrao.TentarObter("1505551", out string? paraNome).Should().BeTrue();
        NomesOficiaisDosMunicipios.Padrao.TentarObter("1716307", out string? tocantinsNome).Should().BeTrue();

        paraNome.Should().Be(tocantinsNome);
    }

    [Fact(DisplayName = "Código fora da lista não tem nome oficial")]
    public void TentarObter_CodigoForaDaLista_NaoAcha()
    {
        NomesOficiaisDosMunicipios.Padrao.TentarObter("1599999", out string? nome).Should().BeFalse();

        nome.Should().BeNull();
    }

    [Fact(DisplayName = "Arquivo bem formado é lido com o texto exato de cada linha")]
    public void Ler_ArquivoValido_GuardaONomeExato()
    {
        NomesOficiaisDosMunicipios lista = Ler($"{Cabecalho}\n1505551\tPA\tPau D'Arco\n");

        lista.Quantidade.Should().Be(1);
        lista.TentarObter("1505551", out string? nome).Should().BeTrue();
        nome.Should().Be("Pau D'Arco");
    }

    [Theory(DisplayName = "Arquivo malformado é recusado inteiro")]
    [InlineData("codigo\tuf\tnome\n1505551\tPA\tPau D'Arco\n", "cabeçalho errado")]
    [InlineData("codigo_ibge\tuf\tnome\n150555\tPA\tPau D'Arco\n", "código com 6 dígitos")]
    [InlineData("codigo_ibge\tuf\tnome\n15055510\tPA\tPau D'Arco\n", "código com 8 dígitos")]
    [InlineData("codigo_ibge\tuf\tnome\n15055AB\tPA\tPau D'Arco\n", "código com letras")]
    [InlineData("codigo_ibge\tuf\tnome\n1505551\tPA\t \n", "nome em branco")]
    [InlineData("codigo_ibge\tuf\tnome\n1505551\tPA\n", "linha sem a coluna do nome")]
    [InlineData("codigo_ibge\tuf\tnome\n1505551\tPA\tPau D'Arco\n1505551\tPA\tPau D'Arco\n", "código repetido")]
    public void Ler_ArquivoMalformado_LancaInvalidDataException(string conteudo, string motivo)
    {
        Action leitura = () => Ler(conteudo);

        leitura.Should().Throw<InvalidDataException>(motivo);
    }

    private static NomesOficiaisDosMunicipios Ler(string conteudo)
    {
        using StringReader leitor = new(conteudo);
        return NomesOficiaisDosMunicipios.Ler(leitor);
    }
}
