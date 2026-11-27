namespace Unifesspa.Geo.IntegrationTests.Proximidade;

using AwesomeAssertions;

using Unifesspa.Geo.API.Formatting;
using Unifesspa.Geo.Kernel.Results;

/// <summary>
/// Validação no boundary HTTP (ADR-0031, #678) dos parâmetros de proximidade:
/// lat/long/raioKm obrigatórios e em faixa, limit com default e teto. Cobre o lado
/// unitário do CA-04 e CA-06 (sem banco).
/// </summary>
public sealed class ConsultaProximidadeTests
{
    private const string Ausentes = "uniplus.geo.proximidade.parametros_ausentes";
    private const string LatitudeInvalida = "uniplus.geo.proximidade.latitude_invalida";
    private const string LongitudeInvalida = "uniplus.geo.proximidade.longitude_invalida";
    private const string RaioInvalido = "uniplus.geo.proximidade.raio_invalido";
    private const string LimiteInvalido = "uniplus.geo.proximidade.limite_invalido";

    private static readonly GeoProximidadeOptions Opcoes = new();

    [Fact(DisplayName = "CA-04: parâmetros válidos criam a consulta com limit default")]
    public void Criar_Valido_UsaLimitDefault()
    {
        Result<ConsultaProximidade> resultado = ConsultaProximidade.Criar(-5.35, -49.13, 100, null, Opcoes);

        resultado.IsSuccess.Should().BeTrue();
        ConsultaProximidade consulta = resultado.Value!;
        consulta.Latitude.Should().Be(-5.35);
        consulta.Longitude.Should().Be(-49.13);
        consulta.RaioKm.Should().Be(100);
        consulta.Limit.Should().Be(Opcoes.LimitPadrao);
    }

    [Fact(DisplayName = "CA-06: limit acima do teto é truncado para o máximo")]
    public void Criar_LimitAcimaDoTeto_Trunca()
    {
        ConsultaProximidade.Criar(0, 0, 10, 100_000, Opcoes).Value!.Limit.Should().Be(Opcoes.LimitMax);
    }

    [Fact(DisplayName = "limit informado dentro do teto é preservado")]
    public void Criar_LimitValido_Preserva()
    {
        ConsultaProximidade.Criar(0, 0, 10, 7, Opcoes).Value!.Limit.Should().Be(7);
    }

    [Theory(DisplayName = "CA-04: parâmetro obrigatório ausente é recusado com o código de parâmetros ausentes")]
    [InlineData(null, -49.13, 100.0)]
    [InlineData(-5.35, null, 100.0)]
    [InlineData(-5.35, -49.13, null)]
    public void Criar_ObrigatorioAusente_Recusa(double? lat, double? lon, double? raio)
    {
        ConferirRecusa(ConsultaProximidade.Criar(lat, lon, raio, null, Opcoes), Ausentes);
    }

    [Theory(DisplayName = "CA-04: lat/long/raio fora de faixa são recusados com o código do parâmetro")]
    [InlineData(91.0, 0.0, 10.0, LatitudeInvalida)]
    [InlineData(-91.0, 0.0, 10.0, LatitudeInvalida)]
    [InlineData(0.0, 181.0, 10.0, LongitudeInvalida)]
    [InlineData(0.0, -181.0, 10.0, LongitudeInvalida)]
    [InlineData(0.0, 0.0, 0.0, RaioInvalido)]
    [InlineData(0.0, 0.0, -5.0, RaioInvalido)]
    [InlineData(0.0, 0.0, 600.0, RaioInvalido)]
    public void Criar_ForaDeFaixa_Recusa(double lat, double lon, double raio, string codigo)
    {
        ConferirRecusa(ConsultaProximidade.Criar(lat, lon, raio, null, Opcoes), codigo);
    }

    [Fact(DisplayName = "CA-04: lat/long/raio NaN ou infinito são recusados com o código do parâmetro")]
    public void Criar_NaoFinito_Recusa()
    {
        ConferirRecusa(ConsultaProximidade.Criar(double.NaN, 0, 10, null, Opcoes), LatitudeInvalida);
        ConferirRecusa(ConsultaProximidade.Criar(0, double.PositiveInfinity, 10, null, Opcoes), LongitudeInvalida);
        ConferirRecusa(ConsultaProximidade.Criar(0, 0, double.PositiveInfinity, null, Opcoes), RaioInvalido);
    }

    [Theory(DisplayName = "CA-04/CA-06: limit <= 0 é recusado com o código do limite, não é silenciado por clamp")]
    [InlineData(0)]
    [InlineData(-1)]
    public void Criar_LimitInvalido_Recusa(int limit)
    {
        ConferirRecusa(ConsultaProximidade.Criar(0, 0, 10, limit, Opcoes), LimiteInvalido);
    }

    [Fact(DisplayName = "A primeira recusa decide: parâmetros ausentes antes de latitude, latitude antes de raio e raio antes de limite")]
    public void Criar_VariasRecusas_PrimeiraDecide()
    {
        ConferirRecusa(ConsultaProximidade.Criar(null, 999, 0, 0, Opcoes), Ausentes);
        ConferirRecusa(ConsultaProximidade.Criar(91, 999, 0, 0, Opcoes), LatitudeInvalida);
        ConferirRecusa(ConsultaProximidade.Criar(0, 999, 0, 0, Opcoes), LongitudeInvalida);
        ConferirRecusa(ConsultaProximidade.Criar(0, 0, 0, 0, Opcoes), RaioInvalido);
    }

    [Fact(DisplayName = "O detalhe do raio cita o máximo da configuração")]
    public void Criar_RaioAcimaDoMaximo_CitaOMaximoConfigurado()
    {
        GeoProximidadeOptions opcoes = new() { RaioMaxKm = 250 };

        ConsultaProximidade.Criar(0, 0, 300, null, opcoes).Error!.Message.Should().Contain("250 km");
    }

    [Fact(DisplayName = "Boundaries de faixa (±90 lat, ±180 long, teto de raio) são aceitos")]
    public void Criar_Boundaries_Ok()
    {
        ConsultaProximidade.Criar(90, 180, Opcoes.RaioMaxKm, null, Opcoes).IsSuccess.Should().BeTrue();
        ConsultaProximidade.Criar(-90, -180, Opcoes.RaioMaxKm, null, Opcoes).IsSuccess.Should().BeTrue();
    }

    private static void ConferirRecusa(Result<ConsultaProximidade> resultado, string codigo)
    {
        resultado.IsFailure.Should().BeTrue();
        resultado.Error!.Code.Should().Be(codigo);
    }

    [Fact(DisplayName = "Options: defaults são válidos (validação fail-fast no boot)")]
    public void Options_Defaults_SaoValidos()
    {
        new GeoProximidadeOptions().LimitesValidos().Should().BeTrue();
    }

    // Parâmetros: (raioMaxKm, limitPadrao, limitMax).
    [Theory(DisplayName = "Options: configuração inválida é rejeitada (RaioMaxKm/LimitMax/LimitPadrao)")]
    [InlineData(0, 50, 200)]      // RaioMaxKm = 0
    [InlineData(-1, 50, 200)]     // RaioMaxKm < 0
    [InlineData(500, 50, 0)]      // LimitMax < 1
    [InlineData(500, 0, 200)]     // LimitPadrao < 1
    [InlineData(500, 300, 200)]   // LimitPadrao > LimitMax
    public void Options_Invalida_Rejeitada(double raioMaxKm, int limitPadrao, int limitMax)
    {
        GeoProximidadeOptions opcoes = new()
        {
            RaioMaxKm = raioMaxKm,
            LimitPadrao = limitPadrao,
            LimitMax = limitMax,
        };

        opcoes.LimitesValidos().Should().BeFalse();
    }
}
