namespace Unifesspa.Geo.Application.DTOs;

/// <summary>
/// Resultado de um envio de pacote aceito: a execução que o representa e se ela é uma carga
/// nova ou a publicação vigente, que já aplicou o mesmo pacote.
/// </summary>
public sealed record EnvioDoPacoteRegistrado(Guid ExecucaoId, DesfechoDoEnvioDoPacote Desfecho);

/// <summary>O que o Geo fez com o pacote recebido.</summary>
public enum DesfechoDoEnvioDoPacote
{
    /// <summary>Uma nova execução foi registrada e enfileirada.</summary>
    CargaEnfileirada = 0,

    /// <summary>O pacote é o mesmo da última carga concluída; nada foi repetido.</summary>
    JaPublicado = 1,
}
