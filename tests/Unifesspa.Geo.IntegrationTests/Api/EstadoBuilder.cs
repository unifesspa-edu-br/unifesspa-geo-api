namespace Unifesspa.Geo.IntegrationTests.Api;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Monta um estado de teste: o nome normalizado vem do nome e o estado nasce vigente,
/// salvo se o teste pedir o contrário.
/// </summary>
internal sealed class EstadoBuilder
{
    private readonly Guid _paisId;
    private readonly string _uf;
    private readonly string _nome;
    private string? _regiao;
    private string? _capital;
    private string? _codigoIbge;
    private decimal? _latitude;
    private decimal? _longitude;
    private bool _vigente = true;

    public EstadoBuilder(Guid paisId, string uf, string nome)
    {
        _paisId = paisId;
        _uf = uf;
        _nome = nome;
    }

    public EstadoBuilder NaRegiao(string regiao)
    {
        _regiao = regiao;
        return this;
    }

    public EstadoBuilder ComCapital(string capital)
    {
        _capital = capital;
        return this;
    }

    public EstadoBuilder ComCodigoIbge(string codigoIbge)
    {
        _codigoIbge = codigoIbge;
        return this;
    }

    public EstadoBuilder NaPosicao(decimal latitude, decimal longitude)
    {
        _latitude = latitude;
        _longitude = longitude;
        return this;
    }

    public EstadoBuilder ForaDeVigencia()
    {
        _vigente = false;
        return this;
    }

    public Estado Construir() =>
        GeoTestKeys.Ok(Estado.Importar(
            _paisId, _uf, _nome, GeoReferenceSeed.Normalizar(_nome), _regiao, _capital, _codigoIbge,
            _latitude, _longitude, null, null, null, GeoReferenceSeed.Versao, _vigente));
}
