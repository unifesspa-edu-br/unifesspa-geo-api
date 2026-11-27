namespace Unifesspa.Geo.IntegrationTests.Etl;

using Unifesspa.Geo.Infrastructure.Persistence.Etl.Fonte;

/// <summary>Monta um registro cru de logradouro para os testes da carga, só com o que o cenário precisa dizer.</summary>
internal sealed class LogradouroCruBuilder
{
    private readonly string _cep;
    private readonly string _nome;
    private readonly int _cidadeIdDne;
    private string _uf = "PA";
    private string _cepAtivo = "S";
    private string? _tipo;
    private string? _nomeCompleto;
    private string? _nomeCompletoSemAcento;
    private int? _bairroIdDne;
    private int? _distritoIdDne;
    private string? _latitude;
    private string? _longitude;

    public LogradouroCruBuilder(string cep, string nome, int cidadeIdDne)
    {
        _cep = cep;
        _nome = nome;
        _cidadeIdDne = cidadeIdDne;
    }

    public LogradouroCruBuilder DoTipo(string tipo)
    {
        _tipo = tipo;
        return this;
    }

    public LogradouroCruBuilder ComNomeCompleto(string nomeCompleto)
    {
        _nomeCompleto = nomeCompleto;
        return this;
    }

    public LogradouroCruBuilder ComNomeCompletoSemAcento(string nomeCompletoSemAcento)
    {
        _nomeCompletoSemAcento = nomeCompletoSemAcento;
        return this;
    }

    public LogradouroCruBuilder NoBairro(int idDne)
    {
        _bairroIdDne = idDne;
        return this;
    }

    public LogradouroCruBuilder NoDistrito(int idDne)
    {
        _distritoIdDne = idDne;
        return this;
    }

    public LogradouroCruBuilder NaPosicao(string latitude, string longitude)
    {
        _latitude = latitude;
        _longitude = longitude;
        return this;
    }

    public LogradouroCruBuilder NaUf(string uf)
    {
        _uf = uf;
        return this;
    }

    public LogradouroCruBuilder ComCepInativo()
    {
        _cepAtivo = "N";
        return this;
    }

    // Espelha a coluna da base dos Correios: sem o texto completo sem acento, cai para o texto
    // completo cru e depois para o nome (a entidade canonicaliza a caixa).
    public LogradouroCru Construir() =>
        new(_cep, _tipo, _nome, _nomeCompleto, _nomeCompletoSemAcento ?? _nomeCompleto ?? _nome,
            _bairroIdDne, _distritoIdDne, _cidadeIdDne, _uf, _latitude, _longitude, _cepAtivo);
}
