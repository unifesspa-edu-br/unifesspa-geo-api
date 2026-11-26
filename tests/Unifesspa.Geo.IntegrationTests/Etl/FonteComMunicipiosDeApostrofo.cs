namespace Unifesspa.Geo.IntegrationTests.Etl;

/// <summary>
/// Fonte da DNE em memória com municípios cujo nome oficial do IBGE tem apóstrofo, na
/// grafia que a DNE entrega (sem o apóstrofo e com a letra seguinte grudada), mais um
/// município com outra divergência de grafia e um sem divergência. Só códigos e nomes
/// públicos do IBGE.
/// </summary>
internal static class FonteComMunicipiosDeApostrofo
{
    public const string PauDArcoPara = "1505551";
    public const string PauDArcoTocantins = "1716307";
    public const string PauDArcoDoPiaui = "2207793";
    public const string SantaBarbaraDOeste = "3545803";
    public const string SantAnaDoLivramento = "4317103";
    public const string EldoradoDoCarajas = "1502954";
    public const string Maraba = "1504208";

    private static readonly (string Uf, string Nome)[] Estados =
    [
        ("PA", "Pará"),
        ("TO", "Tocantins"),
        ("PI", "Piauí"),
        ("SP", "São Paulo"),
        ("RS", "Rio Grande do Sul"),
    ];

    private static readonly (string Codigo, string Nome, string Uf)[] Cidades =
    [
        (PauDArcoPara, "Pau DArco", "PA"),
        (PauDArcoTocantins, "Pau DArco", "TO"),
        (PauDArcoDoPiaui, "Pau DArco do Piauí", "PI"),
        (SantaBarbaraDOeste, "Santa Bárbara DOeste", "SP"),
        (SantAnaDoLivramento, "Santana do Livramento", "RS"),
        (EldoradoDoCarajas, "Eldorado dos Carajás", "PA"),
        (Maraba, "Marabá", "PA"),
    ];

    public static FonteEmMemoria Criar()
    {
        FonteEmMemoria fonte = new() { Versao = "202601" };
        fonte.Paises.Add(DadosDne.Pais("BRA", "Brasil", "BR"));
        fonte.Estados.AddRange(Estados.Select(e => DadosDne.Estado(e.Uf, e.Nome)));
        fonte.Cidades.AddRange(Cidades.Select(c => DadosDne.Cidade(c.Codigo, c.Nome, c.Uf)));
        return fonte;
    }
}
