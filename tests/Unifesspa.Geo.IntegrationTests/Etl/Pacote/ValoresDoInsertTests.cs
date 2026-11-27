namespace Unifesspa.Geo.IntegrationTests.Etl.Pacote;

using AwesomeAssertions;

using Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

/// <summary>A tupla do INSERT só aceita NULL, inteiro de 32 bits e texto entre aspas simples.</summary>
public sealed class ValoresDoInsertTests
{
    [Fact(DisplayName = "Lê NULL, inteiros com e sem sinal e textos com aspa dobrada")]
    public void ValoresDosTresTipos_SaoLidos()
    {
        bool lido = ValoresDoInsert.TentarLer("NULL, 12, -7, +3, 'a', 'it''s', ''", out object?[] valores);

        lido.Should().BeTrue();
        valores.Should().Equal(new object?[] { null, 12, -7, 3, "a", "it's", string.Empty });
    }

    [Fact(DisplayName = "Vírgula, parêntese e ponto e vírgula dentro do texto são dado")]
    public void SeparadoresDentroDoTexto_SaoDado()
    {
        bool lido = ValoresDoInsert.TentarLer("'a, b); c', 1", out object?[] valores);

        lido.Should().BeTrue();
        valores.Should().Equal("a, b); c", 1);
    }

    [Fact(DisplayName = "Limites do inteiro de 32 bits são respeitados")]
    public void LimitesDoInteiro_SaoRespeitados()
    {
        ValoresDoInsert.TentarLer("2147483647, -2147483648", out object?[] dentro).Should().BeTrue();
        ValoresDoInsert.TentarLer("2147483648", out _).Should().BeFalse();
        ValoresDoInsert.TentarLer("-2147483649", out _).Should().BeFalse();
        dentro.Should().Equal(int.MaxValue, int.MinValue);
    }

    [Theory(DisplayName = "Forma fora da gramática é recusada")]
    [InlineData("")]
    [InlineData("E'abc'")]
    [InlineData("e'abc'")]
    [InlineData("$$abc$$")]
    [InlineData("$tag$abc$tag$")]
    [InlineData("'a'::text")]
    [InlineData("1::int")]
    [InlineData("now()")]
    [InlineData("nextval('seq')")]
    [InlineData("null")]
    [InlineData("NULLX")]
    [InlineData("1.5")]
    [InlineData("0x10")]
    [InlineData("1e3")]
    [InlineData("+")]
    [InlineData("'a'; DROP TABLE x")]
    [InlineData("'a') , ('b'")]
    [InlineData("1,")]
    [InlineData(",1")]
    [InlineData("1,,2")]
    [InlineData("'sem fim")]
    [InlineData("'a\0b'")]
    public void FormaForaDaGramatica_EhRecusada(string tupla)
    {
        ValoresDoInsert.TentarLer(tupla, out object?[] valores).Should().BeFalse();
        valores.Should().BeEmpty();
    }
}
