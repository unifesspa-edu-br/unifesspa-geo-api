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

    [Theory(DisplayName = "Código de erro da importação é registrado com o status, o código e o título esperados")]
    [InlineData(GeoImportacaoErrorCodes.VersaoFormatoInvalido, StatusCodes.Status422UnprocessableEntity, "uniplus.geo.importacao.versao_formato_invalido", "Versão do dataset deve estar no formato AAAAMM")]
    [InlineData(GeoImportacaoErrorCodes.DisparadoPorObrigatorio, StatusCodes.Status422UnprocessableEntity, "uniplus.geo.importacao.disparado_por_obrigatorio", "A identificação de quem disparou a carga é obrigatória")]
    [InlineData(GeoImportacaoErrorCodes.ImportacaoEmAndamento, StatusCodes.Status409Conflict, "uniplus.geo.importacao.em_andamento", "Já existe uma importação do Geo em andamento")]
    [InlineData(GeoImportacaoErrorCodes.NaoEnfileirada, StatusCodes.Status503ServiceUnavailable, "uniplus.geo.importacao.nao_enfileirada", "Não foi possível enfileirar a importação (serviço em desligamento)")]
    [InlineData(GeoImportacaoErrorCodes.VersaoNaoProgressiva, StatusCodes.Status409Conflict, "uniplus.geo.importacao.versao_nao_progressiva", "A versão informada é anterior à última release já aplicada")]
    [InlineData(GeoImportacaoErrorCodes.TransicaoInvalida, StatusCodes.Status409Conflict, "uniplus.geo.importacao.transicao_invalida", "Transição de estado da importação inválida")]
    [InlineData(GeoImportacaoErrorCodes.PacoteInvalido, StatusCodes.Status422UnprocessableEntity, "uniplus.geo.importacao.pacote_invalido", "O pacote enviado não é válido")]
    [InlineData(GeoImportacaoErrorCodes.PacoteAcimaDoLimite, StatusCodes.Status413PayloadTooLarge, "uniplus.geo.importacao.pacote_acima_do_limite", "O pacote enviado é maior que o limite permitido")]
    [InlineData(GeoImportacaoErrorCodes.TipoDeConteudoNaoSuportado, StatusCodes.Status415UnsupportedMediaType, "uniplus.geo.importacao.tipo_de_conteudo_nao_suportado", "O envio deve ser um arquivo zip (application/zip)")]
    public void CodigoDaImportacao_TemStatusCodigoETituloEsperados(string codigo, int status, string endereco, string titulo)
    {
        Mapeamentos.Should().ContainKey(codigo);
        Mapeamentos[codigo].Status.Should().Be(status);
        Mapeamentos[codigo].Code.Should().Be(endereco);
        Mapeamentos[codigo].Title.Should().Be(titulo);
    }

    [Fact(DisplayName = "O registro mapeia exatamente os nove códigos conferidos acima")]
    public void Registro_MapeiaExatamenteOsCodigosConferidos()
    {
        Mapeamentos.Should().HaveCount(9);
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
