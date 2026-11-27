namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

using System.Text;

/// <summary>
/// Um <c>INSERT</c> que cruza linhas porque um texto entre aspas simples tem quebra de linha dentro.
/// Guarda o comando com os mesmos fins de linha do arquivo e confere os limites antes de cada junção.
/// Aspa dobrada soma duas, então só a paridade das aspas diz se o texto continua aberto.
/// </summary>
internal sealed class InsertEmVariasLinhas
{
    private readonly StringBuilder _texto;
    private string _fimDeLinhaPendente;
    private int _aspas;

    public InsertEmVariasLinhas(string linhaDeInicio, int numeroDaLinhaDeInicio, string fimDeLinha)
    {
        ArgumentNullException.ThrowIfNull(linhaDeInicio);
        ArgumentNullException.ThrowIfNull(fimDeLinha);
        LinhaDeInicio = linhaDeInicio;
        NumeroDaLinhaDeInicio = numeroDaLinhaDeInicio;
        _texto = new StringBuilder(linhaDeInicio);
        _fimDeLinhaPendente = fimDeLinha;
        _aspas = ContarAspas(linhaDeInicio);
        Bytes = Encoding.UTF8.GetByteCount(linhaDeInicio);
        Linhas = 1;
    }

    public string LinhaDeInicio { get; }

    public int NumeroDaLinhaDeInicio { get; }

    public int Linhas { get; private set; }

    public long Bytes { get; private set; }

    /// <summary>O comando acumulado, com os fins de linha como foram lidos.</summary>
    public string Comando => _texto.ToString();

    /// <summary>Verdadeiro enquanto há uma aspa simples sem par: o texto ainda não fechou.</summary>
    public bool TextoAberto => _aspas % 2 == 1;

    /// <summary>
    /// Lê, a partir da linha de início já lida, as linhas seguintes até o texto fechar. Recusa, citando
    /// a linha de início, quando o arquivo termina antes ou algum limite é ultrapassado.
    /// </summary>
    public static async Task<InsertEmVariasLinhas> LerAsync(
        LinhasDoArquivoSql linhas,
        string linhaDeInicio,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(linhas);
        InsertEmVariasLinhas insert = new(linhaDeInicio, linhas.NumeroDaLinha, linhas.FimDeLinha);
        while (insert.TextoAberto)
        {
            string linha = await linhas.LerLinhaAsync(cancellationToken).ConfigureAwait(false)
                ?? throw linhas.Recusa(MotivosDeRecusaDoArquivoSqlDne.ArquivoIncompleto, insert.NumeroDaLinhaDeInicio);
            string? motivo = insert.Juntar(linha, linhas.FimDeLinha);
            if (motivo is not null)
            {
                throw linhas.Recusa(motivo, insert.NumeroDaLinhaDeInicio);
            }
        }

        return insert;
    }

    public static int ContarAspas(string linha) => linha.AsSpan().Count('\'');

    /// <summary>
    /// Junta a próxima linha, depois de conferir os limites. Devolve o motivo da recusa, ou
    /// <see langword="null"/> quando juntou. <paramref name="fimDeLinha"/> é o fim da linha que está sendo juntada.
    /// </summary>
    public string? Juntar(string linha, string fimDeLinha)
    {
        ArgumentNullException.ThrowIfNull(linha);
        ArgumentNullException.ThrowIfNull(fimDeLinha);
        long acrescimo = _fimDeLinhaPendente.Length + Encoding.UTF8.GetByteCount(linha);
        string? motivo = ConferirLimites(acrescimo);
        if (motivo is not null)
        {
            return motivo;
        }

        _texto.Append(_fimDeLinhaPendente).Append(linha);
        _fimDeLinhaPendente = fimDeLinha;
        _aspas += ContarAspas(linha);
        Bytes += acrescimo;
        Linhas++;
        return null;
    }

    private string? ConferirLimites(long acrescimo)
    {
        if (Linhas + 1 > LimitesDoPacoteDne.LinhasPorComando)
        {
            return MotivosDeRecusaDoArquivoSqlDne.TextoSemFechamento;
        }

        return Bytes + acrescimo > LimitesDoPacoteDne.BytesPorComando
            ? MotivosDeRecusaDoArquivoSqlDne.ComandoAcimaDoLimite
            : null;
    }
}
