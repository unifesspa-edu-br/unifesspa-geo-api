namespace Unifesspa.Geo.Infrastructure.Persistence.Etl.Pacote;

/// <summary>
/// A leitura de um arquivo SQL do pacote foi recusada. A mensagem traz só o nome da tabela
/// (da lista fechada), o número da linha e um motivo fixo: nunca o conteúdo do arquivo.
/// </summary>
public sealed class ErroDeLeituraDoPacoteDneException : Exception
{
    public ErroDeLeituraDoPacoteDneException()
    {
    }

    public ErroDeLeituraDoPacoteDneException(string message)
        : base(message)
    {
    }

    public ErroDeLeituraDoPacoteDneException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    internal ErroDeLeituraDoPacoteDneException(string tabela, int linha, string motivo)
        : base($"Tabela {tabela}, linha {linha}: {motivo}")
    {
        Tabela = tabela;
        Linha = linha;
    }

    public string Tabela { get; } = string.Empty;

    public int Linha { get; }
}
