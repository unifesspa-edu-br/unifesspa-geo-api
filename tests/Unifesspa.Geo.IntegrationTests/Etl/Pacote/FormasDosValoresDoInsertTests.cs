namespace Unifesspa.Geo.IntegrationTests.Etl.Pacote;

using AwesomeAssertions;

using Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

/// <summary>Espaços ao redor dos valores da tupla do INSERT e formas incompletas que a gramática recusa.</summary>
public sealed class FormasDosValoresDoInsertTests
{
    [Theory(DisplayName = "Espaço e tabulação antes e depois de cada valor são aceitos")]
    [InlineData(" 1 , 'a' , NULL ")]
    [InlineData("\t1\t,\t'a'\t,\tNULL\t")]
    [InlineData(" \t1 \t,\t 'a' \t,\t NULL\t ")]
    public void EspacoETabulacao_SaoAceitos(string tupla)
    {
        bool lido = ValoresDoInsert.TentarLer(tupla, out object?[] valores);

        lido.Should().BeTrue();
        valores.Should().Equal(new object?[] { 1, "a", null });
    }

    [Fact(DisplayName = "Espaço dentro do texto entre aspas é dado e não é aparado")]
    public void EspacoDentroDoTexto_EhDado()
    {
        bool lido = ValoresDoInsert.TentarLer("' a\tb '", out object?[] valores);

        lido.Should().BeTrue();
        valores.Should().Equal(" a\tb ");
    }

    [Theory(DisplayName = "Sinal sem dígito, dois inteiros sem vírgula, NULL incompleto e texto com aspa dobrada no fim são recusados")]
    [InlineData("-")]
    [InlineData("1 2")]
    [InlineData("N")]
    [InlineData("'a''")]
    public void FormaIncompleta_EhRecusada(string tupla)
    {
        bool lido = ValoresDoInsert.TentarLer(tupla, out object?[] valores);

        lido.Should().BeFalse();
        valores.Should().BeEmpty();
    }
}
