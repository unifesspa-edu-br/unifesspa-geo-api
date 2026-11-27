namespace Unifesspa.Geo.IntegrationTests.Api;

using NetTopologySuite.Geometries;

using Unifesspa.Geo.Domain.Entities;
using Unifesspa.Geo.Domain.ValueObjects;
using Unifesspa.Geo.IntegrationTests.Infrastructure;

/// <summary>
/// Monta um logradouro de teste como o ETL real: o nome vem sem o tipo, o nome completo traz o
/// texto inteiro e o nome normalizado é o texto completo sem acento (tipo e nome), usado na
/// busca e como chave de atualização.
/// </summary>
internal sealed class LogradouroBuilder
{
    private readonly Guid _cidadeId;
    private readonly string _uf;
    private readonly string _cep;
    private readonly string _nome;
    private string? _tipo;
    private string? _nomeCompleto;
    private Guid? _bairroId;
    private decimal? _latitude;
    private decimal? _longitude;
    private Point? _coordenada;
    private bool _vigente = true;

    public LogradouroBuilder(Guid cidadeId, string uf, string cep, string nome)
    {
        _cidadeId = cidadeId;
        _uf = uf;
        _cep = cep;
        _nome = nome;
    }

    public LogradouroBuilder DoTipo(string tipo)
    {
        _tipo = tipo;
        return this;
    }

    public LogradouroBuilder ComNomeCompleto(string nomeCompleto)
    {
        _nomeCompleto = nomeCompleto;
        return this;
    }

    public LogradouroBuilder NoBairro(Guid bairroId)
    {
        _bairroId = bairroId;
        return this;
    }

    public LogradouroBuilder NaPosicao(decimal latitude, decimal longitude)
    {
        _latitude = latitude;
        _longitude = longitude;
        return this;
    }

    public LogradouroBuilder ComCoordenada(Point coordenada)
    {
        _coordenada = coordenada;
        return this;
    }

    public LogradouroBuilder ForaDeVigencia()
    {
        _vigente = false;
        return this;
    }

    public Logradouro Construir() =>
        GeoTestKeys.Ok(Logradouro.Importar(
            _cep,
            new NomeDoLogradouro(_tipo, _nome, _nomeCompleto, GeoReferenceSeed.Normalizar(_nomeCompleto ?? _nome)),
            new LocalidadeDoLogradouro(_cidadeId, null, _bairroId, _uf),
            new PosicaoGeografica(_latitude, _longitude, _coordenada),
            true,
            GeoReferenceSeed.Proveniencia(_vigente)));
}
