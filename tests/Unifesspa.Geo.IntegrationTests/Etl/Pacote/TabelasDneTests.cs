namespace Unifesspa.Geo.IntegrationTests.Etl.Pacote;

using AwesomeAssertions;

using Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

/// <summary>As 15 tabelas do pacote e o esquema que a carga exige de cada uma.</summary>
public sealed class TabelasDneTests
{
    [Fact(DisplayName = "O pacote tem 15 tabelas, sem nome repetido")]
    public void Nomes_SaoQuinzeSemRepeticao()
    {
        TabelasDne.Nomes.Should().HaveCount(15).And.OnlyHaveUniqueItems();
    }

    [Fact(DisplayName = "Toda tabela exige colunas, sem nome de coluna repetido")]
    public void Colunas_NaoSaoVaziasNemRepetidas()
    {
        foreach (string tabela in TabelasDne.Nomes)
        {
            IReadOnlyList<ColunaDne> colunas = TabelasDne.ColunasObrigatorias(tabela);

            colunas.Should().NotBeEmpty();
            colunas.Select(c => c.Nome).Should().OnlyHaveUniqueItems();
        }
    }

    [Fact(DisplayName = "Os identificadores e chaves de ligação são inteiros e os demais campos são texto")]
    public void Tipos_SeguemAFonte()
    {
        TabelasDne.ColunasObrigatorias("logradouro").Where(c => c.Tipo == TipoDeColunaDne.Inteiro)
            .Select(c => c.Nome).Should().BeEquivalentTo("bairro_id", "distrito_id", "cidade_id");
        TabelasDne.ColunasObrigatorias("cidade").Should().Contain(new ColunaDne("id_cidade", TipoDeColunaDne.Inteiro))
            .And.Contain(new ColunaDne("cidade_ibge", TipoDeColunaDne.Texto));
    }

    [Fact(DisplayName = "Tabela fora da lista não existe e não tem esquema")]
    public void TabelaForaDaLista_NaoExiste()
    {
        TabelasDne.Contem("pg_user").Should().BeFalse();
        Action acao = () => TabelasDne.ColunasObrigatorias("pg_user");
        acao.Should().Throw<ArgumentException>();
    }
}
