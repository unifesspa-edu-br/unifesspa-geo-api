namespace Unifesspa.Geo.IntegrationTests.Etl;

using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;

/// <summary>Entrega a fonte em memória cadastrada para cada versão pedida.</summary>
internal sealed class FabricaDeFonteFixa : IGeoFonteDadosFactory
{
    private readonly Dictionary<string, IGeoFonteDados> _porVersao;

    public FabricaDeFonteFixa(params IGeoFonteDados[] fontes)
    {
        ArgumentNullException.ThrowIfNull(fontes);

        _porVersao = fontes.ToDictionary(f => f.Versao, StringComparer.Ordinal);
    }

    public IGeoFonteDados Criar(string versao) => _porVersao[versao];
}
