namespace Unifesspa.Geo.IntegrationTests.Etl;

using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;

/// <summary>Monta um registro cru de estado para os testes da carga, só com o que o cenário precisa dizer.</summary>
internal sealed class EstadoCruBuilder
{
    private readonly string _uf;
    private readonly string _nome;
    private string? _regiao = "Norte";
    private string? _capital;
    private string? _faixaInicio;
    private string? _faixaFim;
    private string? _latitude;
    private string? _longitude;

    public EstadoCruBuilder(string uf, string nome)
    {
        _uf = uf;
        _nome = nome;
    }

    public EstadoCruBuilder NaRegiao(string regiao)
    {
        _regiao = regiao;
        return this;
    }

    public EstadoCruBuilder ComCapital(string capital)
    {
        _capital = capital;
        return this;
    }

    public EstadoCruBuilder NaFaixaDeCep(string inicio, string fim)
    {
        _faixaInicio = inicio;
        _faixaFim = fim;
        return this;
    }

    public EstadoCruBuilder NaPosicao(string latitude, string longitude)
    {
        _latitude = latitude;
        _longitude = longitude;
        return this;
    }

    public EstadoCru Construir() =>
        new(_uf, _nome, NomeSemAcento: _nome, _regiao, _capital, _faixaInicio, _faixaFim, _latitude, _longitude);
}
