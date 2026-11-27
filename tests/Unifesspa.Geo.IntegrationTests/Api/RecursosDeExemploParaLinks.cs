namespace Unifesspa.Geo.IntegrationTests.Api;

using Unifesspa.Geo.Application.DTOs;

/// <summary>
/// Recursos de exemplo (Marabá, no Pará, CEP 68500000) usados para conferir os links de navegação
/// de cada tipo de resposta. Só dados públicos de localidade.
/// </summary>
internal static class RecursosDeExemploParaLinks
{
    private const string CodigoIbgeDeMaraba = "1500402";
    private const string UfDoPara = "PA";
    private const string CepDeMaraba = "68500000";

    public static CidadeDetalheDto CidadeDetalhe() =>
        new(Guid.NewGuid(), CodigoIbgeDeMaraba, "Marabá", UfDoPara, null, null, null, null, null, null, null, null);

    public static EstadoDto Estado() =>
        new(Guid.NewGuid(), UfDoPara, "Pará", null, null, null, null, null);

    public static CepResolvidoDto CepResolvido() =>
        new(CepDeMaraba, null, null, null, null, null, "Marabá", CodigoIbgeDeMaraba, UfDoPara, null, null, "CEP_GERAL", "TESTE");

    public static LogradouroProximoDto LogradouroProximo() =>
        new(Guid.NewGuid(), CepDeMaraba, null, "Rua de Exemplo", "Marabá", CodigoIbgeDeMaraba, UfDoPara, 0m, 0m, 0d);

    public static LogradouroResumoDto LogradouroResumo() =>
        new(Guid.NewGuid(), CepDeMaraba, null, "Rua de Exemplo", null, null, CodigoIbgeDeMaraba, UfDoPara);

    public static DistritoDto Distrito() =>
        new(Guid.NewGuid(), "Distrito de Exemplo", UfDoPara, CodigoIbgeDeMaraba, null, null);

    public static BairroDto Bairro() =>
        new(Guid.NewGuid(), "Bairro de Exemplo", UfDoPara, CodigoIbgeDeMaraba, null, null);

    public static CidadeResumoDto CidadeResumo() =>
        new(Guid.NewGuid(), CodigoIbgeDeMaraba, "Marabá", UfDoPara, null);

    public static CidadeProximaDto CidadeProxima() =>
        new(Guid.NewGuid(), CodigoIbgeDeMaraba, "Marabá", UfDoPara, 0m, 0m, 0d);

    public static ImportacaoGeoDto Importacao(Guid id) =>
        new(id, "2026-01", "CONCLUIDA", DateTimeOffset.UnixEpoch, null, "teste", null, null, null);
}
