namespace Unifesspa.Geo.Infrastructure.Core.Cryptography;

/// <summary>
/// Ponto único da regra "qualquer falha que não seja <see cref="EncryptionFailureException"/>
/// vira <see cref="EncryptionFailureException"/> com o nome da chave e a falha original".
/// </summary>
internal static class TraducaoDaFalhaDeCifragem
{
    internal static T Executar<T>(string keyName, Func<T> operacao)
    {
        try
        {
            return operacao();
        }
        catch (Exception ex) when (ex is not EncryptionFailureException)
        {
            throw new EncryptionFailureException(keyName, ex);
        }
    }

    internal static async Task<T> ExecutarAsync<T>(string keyName, Func<Task<T>> operacao)
    {
        try
        {
            return await operacao().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not EncryptionFailureException)
        {
            throw new EncryptionFailureException(keyName, ex);
        }
    }
}
