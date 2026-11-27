namespace Unifesspa.Geo.IntegrationTests.Etl.Pacote;

using System.Text;

using AwesomeAssertions;

using Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

/// <summary>
/// Como o arquivo SQL do pacote é separado em linhas: a marca de ordem dos bytes só vale na
/// primeira linha, o limite de 64 KiB conta a linha sem o fim de linha e o fim de linha é
/// informado como foi lido.
/// </summary>
public sealed class LeituraDasLinhasDoArquivoSqlTests
{
    private const string Tabela = "cidade_faixa";
    private static readonly byte[] MarcaDeOrdem = Encoding.UTF8.GetPreamble();

    [Fact(DisplayName = "A marca de ordem dos bytes sai da primeira linha e, em outra linha, fica no texto")]
    public async Task MarcaDeOrdem_SoSaiDaPrimeiraLinha()
    {
        byte[] bytes = [.. MarcaDeOrdem, .. "abc\r\n"u8.ToArray(), .. MarcaDeOrdem, .. "def\r\n"u8.ToArray()];

        List<(string Texto, string FimDeLinha)> lidas = await LerTudoAsync(bytes);

        lidas.Should().Equal(("abc", "\r\n"), ("﻿def", "\r\n"));
    }

    [Theory(DisplayName = "Linha de exatamente 64 KiB, sem o fim de linha, é aceita com CRLF e com LF")]
    [InlineData("\r\n")]
    [InlineData("\n")]
    public async Task LinhaDeExatos64KiB_EhAceita(string fimDeLinha)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(new string('a', LimitesDoPacoteDne.BytesPorLinha) + fimDeLinha);

        List<(string Texto, string FimDeLinha)> lidas = await LerTudoAsync(bytes);

        lidas.Should().ContainSingle().Which.Texto.Length.Should().Be(LimitesDoPacoteDne.BytesPorLinha);
    }

    [Theory(DisplayName = "Linha com um byte a mais que 64 KiB é recusada na própria linha, com CRLF e com LF")]
    [InlineData("\r\n")]
    [InlineData("\n")]
    public async Task LinhaComUmByteAMais_EhRecusada(string fimDeLinha)
    {
        byte[] bytes = Encoding.UTF8.GetBytes("x\n" + new string('a', LimitesDoPacoteDne.BytesPorLinha + 1) + fimDeLinha);

        Func<Task> leitura = () => LerTudoAsync(bytes);

        (await leitura.Should().ThrowAsync<ErroDeLeituraDoPacoteDneException>()).Which.Linha.Should().Be(2);
    }

    [Fact(DisplayName = "Última linha sem quebra é lida, sem fim de linha, e depois dela o arquivo termina sem contar linha a mais")]
    public async Task UltimaLinhaSemQuebra_EhLidaEOArquivoTermina()
    {
        using MemoryStream fluxo = new("a\r\nb"u8.ToArray());
        LinhasDoArquivoSql linhas = new(fluxo, Tabela, long.MaxValue);

        string? primeira = await linhas.LerLinhaAsync(CancellationToken.None);
        string? ultima = await linhas.LerLinhaAsync(CancellationToken.None);
        string? depois = await linhas.LerLinhaAsync(CancellationToken.None);

        (primeira, ultima, depois, linhas.FimDeLinha, linhas.NumeroDaLinha).Should().Be(("a", "b", (string?)null, string.Empty, 2));
    }

    [Fact(DisplayName = "CR antes de LF sai do texto e entra no fim de linha, linha a linha")]
    public async Task CrAntesDeLf_SaiDoTextoEEntraNoFimDeLinha()
    {
        List<(string Texto, string FimDeLinha)> lidas = await LerTudoAsync("a\r\nb\nc"u8.ToArray());

        lidas.Should().Equal(("a", "\r\n"), ("b", "\n"), ("c", string.Empty));
    }

    private static async Task<List<(string Texto, string FimDeLinha)>> LerTudoAsync(byte[] bytes)
    {
        using MemoryStream fluxo = new(bytes);
        LinhasDoArquivoSql linhas = new(fluxo, Tabela, long.MaxValue);
        List<(string Texto, string FimDeLinha)> lidas = [];
        while (await linhas.LerLinhaAsync(CancellationToken.None) is { } texto)
        {
            lidas.Add((texto, linhas.FimDeLinha));
        }

        return lidas;
    }
}
