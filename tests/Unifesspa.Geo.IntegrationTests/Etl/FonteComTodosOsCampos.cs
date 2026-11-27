namespace Unifesspa.Geo.IntegrationTests.Etl;

using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;

/// <summary>
/// Fontes em memória com todos os campos preenchidos, para conferir cada coluna gravada pela carga.
/// A primeira chega com espaços nas pontas, UF em minúsculas, forma de busca em maiúsculas, opcionais
/// em branco e coordenada fora da faixa; a segunda traz as mesmas chaves com os valores trocados.
/// Só há dados públicos ou fictícios, sem pessoa.
/// </summary>
internal static class FonteComTodosOsCampos
{
    public static FonteEmMemoria Primeira()
    {
        FonteEmMemoria fonte = new() { Versao = " 202601 " };
        AdicionarTopoDaPrimeira(fonte);
        AdicionarCidadesDaPrimeira(fonte);
        AdicionarDistritosEBairrosDaPrimeira(fonte);
        AdicionarCepsDaPrimeira(fonte);
        AdicionarLogradourosDaPrimeira(fonte);
        return fonte;
    }

    public static FonteEmMemoria Segunda()
    {
        FonteEmMemoria fonte = new() { Versao = "202602" };
        AdicionarTopoDaSegunda(fonte);
        AdicionarCidadesDaSegunda(fonte);
        AdicionarDistritosEBairrosDaSegunda(fonte);
        AdicionarCepsDaSegunda(fonte);
        AdicionarLogradourosDaSegunda(fonte);
        return fonte;
    }

    /// <summary>As linhas da primeira seguidas das da segunda, com as mesmas chaves: a última ocorrência vale.</summary>
    public static FonteEmMemoria ComChaveRepetida()
    {
        FonteEmMemoria juntas = new() { Versao = "202602" };
        foreach (FonteEmMemoria origem in new[] { Primeira(), Segunda() })
        {
            AcrescentarTopo(juntas, origem);
            AcrescentarFolhas(juntas, origem);
            AcrescentarCeps(juntas, origem);
        }

        return juntas;
    }

    private static void AcrescentarTopo(FonteEmMemoria destino, FonteEmMemoria origem)
    {
        destino.Paises.AddRange(origem.Paises);
        destino.Estados.AddRange(origem.Estados);
        destino.EstadoIndicadores.AddRange(origem.EstadoIndicadores);
        destino.EstadoFaixas.AddRange(origem.EstadoFaixas);
        destino.Cidades.AddRange(origem.Cidades);
        destino.CidadeTerritorios.AddRange(origem.CidadeTerritorios);
        destino.CidadeIndicadores.AddRange(origem.CidadeIndicadores);
        destino.CidadeFaixas.AddRange(origem.CidadeFaixas);
        destino.CidadeIds.AddRange(origem.CidadeIds);
    }

    private static void AcrescentarFolhas(FonteEmMemoria destino, FonteEmMemoria origem)
    {
        destino.Distritos.AddRange(origem.Distritos);
        destino.DistritoFaixas.AddRange(origem.DistritoFaixas);
        destino.Bairros.AddRange(origem.Bairros);
        destino.BairroFaixas.AddRange(origem.BairroFaixas);
    }

    private static void AcrescentarCeps(FonteEmMemoria destino, FonteEmMemoria origem)
    {
        destino.CepGrandesUsuarios.AddRange(origem.CepGrandesUsuarios);
        destino.CepGerais.AddRange(origem.CepGerais);
        destino.LogradouroComplementos.AddRange(origem.LogradouroComplementos);
        destino.Logradouros.AddRange(origem.Logradouros);
    }

    private static void AdicionarTopoDaPrimeira(FonteEmMemoria fonte)
    {
        fonte.Paises.Add(new PaisCru(" bra ", " br ", " Brasil ", " 1058 ", "   ", " 0105 ", null));
        fonte.Estados.Add(new EstadoCru(" pa ", " Pará ", " PARA ", " Norte ", " Belém ", " 66000000 ", " 68899999 ", " -5.36867 ", " -49.11731 "));
        fonte.Estados.Add(new EstadoCru("sp", " São Paulo ", "   ", "   ", null, null, "  ", "95.5", "-200"));
        fonte.EstadoIndicadores.Add(new EstadoIndicadorCru(" pa ", " 15 ", " paraense ", " Governador Fictício A ", "1245870.704", " 8120131 ", "6.52", "1000000", "0.646", "50000.50", "40000.25", "1200", "2500000"));
        fonte.EstadoIndicadores.Add(new EstadoIndicadorCru("sp", "  ", "  ", null, "-", "abc", "", null, "0.826", "-", " ", null, "30000000"));
        fonte.EstadoFaixas.Add(new EstadoFaixaCru(" pa ", " Capital ", " 66000000 ", " 66999999 "));
        fonte.EstadoFaixas.Add(new EstadoFaixaCru("PA", "Interior", "67000000", "68899999"));
        fonte.EstadoFaixas.Add(new EstadoFaixaCru("sp", "   ", "01000000", "05999999"));
    }

    private static void AdicionarTopoDaSegunda(FonteEmMemoria fonte)
    {
        fonte.Paises.Add(new PaisCru("BRA", " bz ", " República Federativa do Brasil ", null, " 2058 ", " 3058 ", " 4058 "));
        fonte.Estados.Add(new EstadoCru("PA", "Estado do Pará", "ESTADO DO PARA", null, "Belém do Pará", " 66000001 ", " 68899998 ", "-3.9", "-52.5"));
        fonte.Estados.Add(new EstadoCru("sp", "São Paulo", " SAO PAULO ESTADO ", " Sudeste ", " São Paulo ", "01000000", "19999999", " -23.55 ", " -46.63 "));
        fonte.EstadoIndicadores.Add(new EstadoIndicadorCru("PA", " 16 ", "paraense (2)", "Governador Fictício B", "1245870.705", "8120132", "6.53", "1000001", "0.647", "50001.51", "40001.26", "1201", "2500001"));
        fonte.EstadoIndicadores.Add(new EstadoIndicadorCru("SP", " 35 ", " paulista ", " Governador Fictício C ", "248219.481", "44411238", "178.9", "5000000", "0.806", "900000.99", "800000.88", "2500", "30000001"));
        fonte.EstadoFaixas.Add(new EstadoFaixaCru("PA", " Capital (Belém) ", "66000000", "66999999"));
        fonte.EstadoFaixas.Add(new EstadoFaixaCru("PA", "Interior do Pará", "67000000", "68899999"));
        fonte.EstadoFaixas.Add(new EstadoFaixaCru("SP", " Capital paulista ", "01000000", "05999999"));
    }

    private static void AdicionarCidadesDaPrimeira(FonteEmMemoria fonte)
    {
        fonte.Cidades.Add(new CidadeCru(" 1500402 ", "  Marabá D'Oeste  ", " pa ", " 94 ", " -5.36867 ", " -49.11731 "));
        fonte.Cidades.Add(new CidadeCru("3550308", " SAO PAULO ", "sp", "11", "-95.5", "200.25"));
        fonte.CidadeTerritorios.Add(new CidadeTerritorioCru(" 1500402 ", " 1506 ", " Sudeste Paraense ", " 15021 ", " Marabá ", " 1502 ", " Marabá ", " 150003 ", "   "));
        fonte.CidadeIndicadores.Add(new CidadeIndicadorCru(" 1500402 ", " marabaense ", " Prefeito Fictício A ", "15128.058", "266533", "17.62", "97.5", "0.668", "14.45", "800000000.25", "750000000.10", "30000.99", " 05/04 "));
        fonte.CidadeIndicadores.Add(new CidadeIndicadorCru("3550308", "   ", null, "1521.11", "-", "7528.26", "-", "0.805", "-", "-", " ", "-", null));
        fonte.CidadeFaixas.Add(new CidadeFaixaCru(1, " 68500000 ", " 68519999 "));
        fonte.CidadeFaixas.Add(new CidadeFaixaCru(2, "01000000", "05999999"));
        fonte.CidadeIds.Add(new CidadeIdCru(1, " 1500402 "));
        fonte.CidadeIds.Add(new CidadeIdCru(2, "3550308"));
    }

    private static void AdicionarCidadesDaSegunda(FonteEmMemoria fonte)
    {
        fonte.Cidades.Add(new CidadeCru("1500402", " Marabá Nova ", "PA", " 91 ", "91", "-181"));
        fonte.Cidades.Add(new CidadeCru("3550308", "SAO PAULO CAPITAL", "SP", null, " -23.55 ", " -46.63 "));
        fonte.CidadeTerritorios.Add(new CidadeTerritorioCru("1500402", " ", "  ", "", null, "  ", " ", "   ", " "));
        fonte.CidadeTerritorios.Add(new CidadeTerritorioCru(" 3550308 ", " 3515 ", " Macrometropolitana Paulista ", " 35061 ", " São Paulo ", " 3501 ", " São Paulo ", " 350001 ", " São Paulo "));
        fonte.CidadeIndicadores.Add(new CidadeIndicadorCru("1500402", "marabaense (2)", "Prefeito Fictício B", "15128.059", "266534", "17.63", "97.6", "0.669", "14.46", "800000001.26", "750000001.11", "30001.00", "06/04"));
        fonte.CidadeIndicadores.Add(new CidadeIndicadorCru("3550308", " paulistano ", " Prefeito Fictício C ", "1521.12", "11451999", "7528.27", "98.1", "0.806", "10.2", "9000000000.50", "8000000000.25", "50000.12", " 25/01 "));
        fonte.CidadeFaixas.Add(new CidadeFaixaCru(1, "68500000", "68519999"));
        fonte.CidadeFaixas.Add(new CidadeFaixaCru(2, "01000000", "05999999"));
        fonte.CidadeIds.Add(new CidadeIdCru(1, "1500402"));
        fonte.CidadeIds.Add(new CidadeIdCru(2, "3550308"));
    }

    private static void AdicionarDistritosEBairrosDaPrimeira(FonteEmMemoria fonte)
    {
        fonte.Distritos.Add(new DistritoCru(10, " Cidade Nova ", " CIDADE NOVA ", 1, " pa ", "-5.36", "-49.11"));
        fonte.Distritos.Add(new DistritoCru(20, "Centro", "CENTRO", 2, "sp", "95.5", "-200"));
        fonte.DistritoFaixas.Add(new FaixaLocalidadeCru(10, " 68500000 ", " 68519999 "));
        fonte.DistritoFaixas.Add(new FaixaLocalidadeCru(20, "01000000", "01099999"));
        fonte.Bairros.Add(new BairroCru(100, " Cidade Nova ", " CIDADE NOVA ", 1, " pa ", " -5.37 ", " -49.12 "));
        fonte.Bairros.Add(new BairroCru(200, "Sé", "SE", 2, "sp", "95.5", "-200"));
        fonte.Bairros.Add(new BairroCru(null, " Bairro Sem Origem ", " BAIRRO SEM ORIGEM ", 1, "pa", null, null));
        fonte.BairroFaixas.Add(new FaixaLocalidadeCru(100, "68500000", "68509999"));
        fonte.BairroFaixas.Add(new FaixaLocalidadeCru(200, "01000000", "01049999"));
    }

    private static void AdicionarDistritosEBairrosDaSegunda(FonteEmMemoria fonte)
    {
        fonte.Distritos.Add(new DistritoCru(10, " Cidade Nova (núcleo) ", "CIDADE NOVA", 1, "pa", " -5.40 ", " -49.15 "));
        fonte.Distritos.Add(new DistritoCru(20, " Centro Histórico ", " CENTRO ", 2, " sp ", " -23.55 ", " -46.63 "));
        fonte.DistritoFaixas.Add(new FaixaLocalidadeCru(10, "68500000", "68519999"));
        fonte.DistritoFaixas.Add(new FaixaLocalidadeCru(20, "01000000", "01099999"));
        fonte.Bairros.Add(new BairroCru(100, "Cidade Nova II", " CIDADE NOVA ", 1, "pa", "95.5", "-200"));
        fonte.Bairros.Add(new BairroCru(200, " Sé (centro) ", "SE", 2, " sp ", " -23.55 ", " -46.63 "));
        fonte.Bairros.Add(new BairroCru(300, "Bairro Sem Origem", "BAIRRO SEM ORIGEM", 1, "pa", null, null));
        fonte.BairroFaixas.Add(new FaixaLocalidadeCru(100, "68500000", "68509999"));
        fonte.BairroFaixas.Add(new FaixaLocalidadeCru(200, "01000000", "01049999"));
    }

    private static void AdicionarCepsDaPrimeira(FonteEmMemoria fonte)
    {
        fonte.CepGrandesUsuarios.Add(new CepGrandeUsuarioCru(" 68500900 ", " Prefeitura de Marabá ", " PREFEITURA DE MARABA "));
        fonte.CepGrandesUsuarios.Add(new CepGrandeUsuarioCru("01000900", " Câmara Municipal ", "  "));
        fonte.CepGerais.Add(new CepGeralCru(" 68525000 ", 1, null, " pa ", " -5.046 ", " -48.59947 "));
        fonte.CepGerais.Add(new CepGeralCru("68525100", 1, 10, "pa", null, null));
        fonte.CepGerais.Add(new CepGeralCru("68525200", 2, null, "sp", "95.5", "-200"));
        fonte.LogradouroComplementos.Add(new LogradouroComplementoCru(" 68500000 ", " lado par ", " LADO PAR "));
        fonte.LogradouroComplementos.Add(new LogradouroComplementoCru(" 68500000 ", " Lado Ímpar ", " LADO IMPAR "));
        fonte.LogradouroComplementos.Add(new LogradouroComplementoCru("01001000", " Sem Texto Normalizado ", null));
    }

    private static void AdicionarCepsDaSegunda(FonteEmMemoria fonte)
    {
        fonte.CepGrandesUsuarios.Add(new CepGrandeUsuarioCru("68500900", " Prefeitura Municipal de Marabá ", null));
        fonte.CepGrandesUsuarios.Add(new CepGrandeUsuarioCru("01000900", "Câmara Municipal de São Paulo", " CAMARA MUNICIPAL DE SAO PAULO "));
        fonte.CepGerais.Add(new CepGeralCru(" 68525000 ", 2, 20, " sp ", "-23.5", "-46.6"));
        fonte.CepGerais.Add(new CepGeralCru("68525100", 1, null, "PA", "-5.05", "-48.6"));
        fonte.CepGerais.Add(new CepGeralCru("68525200", 1, 10, "pa", "", ""));
        fonte.LogradouroComplementos.Add(new LogradouroComplementoCru(" 68500000 ", " Lado Par (revisado) ", " LADO PAR "));
        fonte.LogradouroComplementos.Add(new LogradouroComplementoCru("68500000", " Lado ímpar revisto ", "LADO IMPAR"));
        fonte.LogradouroComplementos.Add(new LogradouroComplementoCru("01001000", " SEM Texto Normalizado ", null));
    }

    private static void AdicionarLogradourosDaPrimeira(FonteEmMemoria fonte)
    {
        fonte.Logradouros.Add(new LogradouroCru(" 68500000 ", " Rua ", " A ", " Rua A ", " RUA A ", 100, 10, 1, " pa ", " -5.36867 ", " -49.11731 ", "S"));
        fonte.Logradouros.Add(new LogradouroCru("68500001", "  ", "Rua B", null, null, null, null, 1, "pa", "95.5", "-200", "N"));
        fonte.Logradouros.Add(new LogradouroCru("01001000", "Praça", " da Sé ", "Praça da Sé", "PRACA DA SE", 200, 20, 2, "sp", "-23.55", "-46.63", "S"));
    }

    private static void AdicionarLogradourosDaSegunda(FonteEmMemoria fonte)
    {
        fonte.Logradouros.Add(new LogradouroCru(" 68500000 ", " Travessa ", " A Nova ", " Travessa A Nova ", " RUA A ", null, null, 1, " pa ", "-5.37", "-49.12", "N"));
        fonte.Logradouros.Add(new LogradouroCru("68500001", "Rua", "B Segunda", "Rua B Segunda", "RUA B", 100, null, 1, "pa", "-5.38", "-49.13", "S"));
        fonte.Logradouros.Add(new LogradouroCru("01001000", "Praça", "da Sé", "Praça da Sé", "PRACA DA SE", null, null, 2, "sp", "95.5", "-200", "S"));
    }
}
