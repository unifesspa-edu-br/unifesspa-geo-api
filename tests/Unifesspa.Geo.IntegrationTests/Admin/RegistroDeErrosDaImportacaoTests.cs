namespace Unifesspa.Geo.IntegrationTests.Admin;

using AwesomeAssertions;

using Microsoft.AspNetCore.Http;

using Unifesspa.Geo.API.Errors;
using Unifesspa.Geo.Domain.Errors;
using Unifesspa.Geo.Infrastructure.Core.Errors;

/// <summary>Cada código de erro do recebimento do pacote tem o status HTTP e o endereço de erro esperados.</summary>
public sealed class RegistroDeErrosDaImportacaoTests
{
    private static readonly Dictionary<string, DomainErrorMapping> Mapeamentos =
        new GeoDomainErrorRegistration().GetMappings().ToDictionary(m => m.Key, m => m.Value);

    [Theory(DisplayName = "Código de erro do pacote é registrado com o status e o endereço de erro esperados")]
    [InlineData(GeoImportacaoErrorCodes.PacoteInvalido, StatusCodes.Status422UnprocessableEntity, "uniplus.geo.importacao.pacote_invalido")]
    [InlineData(GeoImportacaoErrorCodes.PacoteAcimaDoLimite, StatusCodes.Status413PayloadTooLarge, "uniplus.geo.importacao.pacote_acima_do_limite")]
    [InlineData(GeoImportacaoErrorCodes.TipoDeConteudoNaoSuportado, StatusCodes.Status415UnsupportedMediaType, "uniplus.geo.importacao.tipo_de_conteudo_nao_suportado")]
    public void CodigoDoPacote_TemStatusEEnderecoEsperados(string codigo, int status, string endereco)
    {
        Mapeamentos.Should().ContainKey(codigo);
        Mapeamentos[codigo].Status.Should().Be(status);
        Mapeamentos[codigo].Code.Should().Be(endereco);
        Mapeamentos[codigo].Title.Should().NotBeNullOrWhiteSpace();
    }

    [Fact(DisplayName = "Todos os códigos registrados têm endereço de erro único no padrão uniplus.geo.importacao")]
    public void CodigosRegistrados_TemEnderecosUnicosNoPadrao()
    {
        Mapeamentos.Values.Select(m => m.Code).Should().OnlyHaveUniqueItems();
        Mapeamentos.Values.Select(m => m.Code).Should().OnlyContain(c => c.StartsWith("uniplus.geo.importacao.", StringComparison.Ordinal));
    }

    [Theory(DisplayName = "O erro de pacote acima do limite cita o limite formatado, não em bytes")]
    [InlineData(128L * 1024 * 1024, "128 MiB")]
    [InlineData(2048L, "2 KiB")]
    [InlineData(1000L, "1000 bytes")]
    public void PacoteAcimaDoLimite_CitaOLimiteFormatado(long limite, string esperado)
    {
        ErrosDoPacoteDne.PacoteAcimaDoLimite(limite).Message.Should().Contain(esperado);
    }
}
