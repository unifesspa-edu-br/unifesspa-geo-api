namespace Unifesspa.Geo.IntegrationTests.Api;

using NetTopologySuite.Geometries;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Monta uma cidade de teste: sem indicação em contrário, o nome normalizado vem do nome e a
/// cidade nasce vigente.
/// </summary>
internal sealed class CidadeBuilder
{
    private readonly Guid _estadoId;
    private readonly string _uf;
    private readonly string _codigoIbge;
    private readonly string _nome;
    private string? _nomeNormalizado;
    private string? _ddd;
    private decimal? _latitude;
    private decimal? _longitude;
    private Point? _coordenada;
    private string? _mesorregiao;
    private string? _microrregiao;
    private string? _regiaoIntermediaria;
    private string? _regiaoImediata;
    private bool _vigente = true;

    public CidadeBuilder(Guid estadoId, string uf, string codigoIbge, string nome)
    {
        _estadoId = estadoId;
        _uf = uf;
        _codigoIbge = codigoIbge;
        _nome = nome;
    }

    public CidadeBuilder ComNomeNormalizado(string nomeNormalizado)
    {
        _nomeNormalizado = nomeNormalizado;
        return this;
    }

    public CidadeBuilder ComDdd(string ddd)
    {
        _ddd = ddd;
        return this;
    }

    public CidadeBuilder NaPosicao(decimal latitude, decimal longitude)
    {
        _latitude = latitude;
        _longitude = longitude;
        return this;
    }

    public CidadeBuilder ComCoordenada(Point coordenada)
    {
        _coordenada = coordenada;
        return this;
    }

    public CidadeBuilder NaMesorregiao(string nome)
    {
        _mesorregiao = nome;
        return this;
    }

    public CidadeBuilder NaMicrorregiao(string nome)
    {
        _microrregiao = nome;
        return this;
    }

    public CidadeBuilder NaRegiaoIntermediaria(string nome)
    {
        _regiaoIntermediaria = nome;
        return this;
    }

    public CidadeBuilder NaRegiaoImediata(string nome)
    {
        _regiaoImediata = nome;
        return this;
    }

    public CidadeBuilder ForaDeVigencia()
    {
        _vigente = false;
        return this;
    }

    public Cidade Construir() =>
        GeoTestKeys.Ok(Cidade.Importar(
            _estadoId, _uf, _codigoIbge, _nome,
            _nomeNormalizado ?? GeoReferenceSeed.Normalizar(_nome), _ddd, _latitude, _longitude, _coordenada,
            null, _mesorregiao, null, _microrregiao,
            null, _regiaoIntermediaria, null, _regiaoImediata,
            GeoReferenceSeed.Versao, _vigente));
}
