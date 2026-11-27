namespace Unifesspa.Geo.IntegrationTests.Etl;

/// <summary>Versões fictícias para exercitar a carga: a anterior (202601) e a seguinte (202602).</summary>
internal static class VersoesFicticias
{
    public const string VersaoAnterior = "202601";
    public const string VersaoNova = "202602";
    public const string CodigoMaraba = "1500402";
    public const string CodigoSaoPaulo = "3550308";
    public const string CepRuaA = "68500000";
    public const string CepRuaNova = "68500777";
    public const double LatitudeRuaA = -5.36867;
    public const double LongitudeRuaA = -49.11731;

    public static FonteEmMemoria Anterior() => Base(VersaoAnterior, comSaoPaulo: true);

    public static FonteEmMemoria Nova(Exception? falhaAoLerLogradouros = null)
    {
        FonteEmMemoria fonte = Base(VersaoNova, comSaoPaulo: false, falhaAoLerLogradouros);
        fonte.LogradouroComplementos.Add(DadosDne.Complemento(CepRuaA, "lado novo"));
        fonte.Logradouros.Add(new LogradouroCruBuilder(CepRuaNova, "Rua Nova", 1).DoTipo("Rua").NaPosicao("-5.37", "-49.12").Construir());
        return fonte;
    }

    private static FonteEmMemoria Base(string versao, bool comSaoPaulo, Exception? falhaAoLerLogradouros = null)
    {
        FonteEmMemoria fonte = new() { Versao = versao, FalhaAoLerLogradouros = falhaAoLerLogradouros };
        fonte.Paises.Add(DadosDne.Pais("BRA", "Brasil", "BR"));
        AdicionarMaraba(fonte);
        AdicionarLogradourosDeMaraba(fonte);
        if (comSaoPaulo)
        {
            AdicionarSaoPaulo(fonte);
        }

        return fonte;
    }

    private static void AdicionarMaraba(FonteEmMemoria fonte)
    {
        fonte.Estados.Add(new EstadoCruBuilder("PA", "Pará").ComCapital("Belém").NaFaixaDeCep("66000000", "68899999").Construir());
        fonte.EstadoIndicadores.Add(DadosDne.EstadoIndicador("PA", "15"));
        fonte.Cidades.Add(DadosDne.Cidade(CodigoMaraba, "Marabá", "PA", ddd: "94"));
        fonte.CidadeIds.Add(DadosDne.CidadeId(1, CodigoMaraba));
        fonte.Distritos.Add(DadosDne.Distrito(10, "Cidade Nova", 1));
    }

    private static void AdicionarLogradourosDeMaraba(FonteEmMemoria fonte)
    {
        fonte.LogradouroComplementos.Add(DadosDne.Complemento(CepRuaA, "lado par"));
        fonte.Logradouros.Add(new LogradouroCruBuilder(CepRuaA, "Rua A", 1)
            .DoTipo("Rua").NoDistrito(10).NaPosicao("-5.36867", "-49.11731").Construir());
        fonte.Logradouros.Add(new LogradouroCruBuilder("68500001", "Rua B", 1).DoTipo("Rua").Construir());
    }

    private static void AdicionarSaoPaulo(FonteEmMemoria fonte)
    {
        fonte.Estados.Add(new EstadoCruBuilder("SP", "São Paulo").NaRegiao("Sudeste").ComCapital("São Paulo").NaFaixaDeCep("01000000", "19999999").Construir());
        fonte.EstadoIndicadores.Add(DadosDne.EstadoIndicador("SP", "35"));
        fonte.Cidades.Add(DadosDne.Cidade(CodigoSaoPaulo, "São Paulo", "SP", ddd: "11"));
        fonte.CidadeIds.Add(DadosDne.CidadeId(2, CodigoSaoPaulo));
        fonte.Logradouros.Add(new LogradouroCruBuilder("01000000", "Av Paulista", 2).NaUf("SP").DoTipo("Avenida").Construir());
    }
}
