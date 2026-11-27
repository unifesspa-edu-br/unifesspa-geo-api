namespace Unifesspa.Geo.Infrastructure.Core.Cryptography;

using VaultSharp;
using VaultSharp.Core;
using VaultSharp.V1.AuthMethods;
using VaultSharp.V1.AuthMethods.Kubernetes;
using VaultSharp.V1.AuthMethods.Token;

/// <summary>
/// Conexão autenticada ao Vault, com o cliente atual e a renovação dele quando a
/// autenticação é recusada (403). Guarda o estado e o ciclo de vida da conexão.
/// </summary>
internal sealed class ConexaoAoVault : IDisposable
{
    private volatile VaultClient _cliente;
    private readonly string _endereco;
    private readonly string _caminhoDoJwt;
    private readonly string? _role;
    private readonly string? _token;
    private readonly bool _usaAutenticacaoKubernetes;
    private readonly SemaphoreSlim _trocaDoCliente = new(1, 1);

    public ConexaoAoVault(EncryptionOptions opcoes)
    {
        ArgumentNullException.ThrowIfNull(opcoes);

        _endereco = ExigirEndereco(opcoes.VaultAddress);
        _usaAutenticacaoKubernetes = UsaAutenticacaoKubernetes(opcoes);
        _caminhoDoJwt = opcoes.KubernetesJwtPath;
        _role = opcoes.KubernetesRole;
        _token = opcoes.VaultToken;

        _cliente = CriarCliente();
    }

    /// <summary>
    /// Executa <paramref name="operacao"/> com repetição automática quando o Vault retorna 403.
    /// A repetição relê o JWT do disco, garantindo que tokens K8s rotacionados sejam absorvidos sem restart.
    /// </summary>
    public async Task<T> ExecutarComReautenticacaoAsync<T>(Func<VaultClient, Task<T>> operacao, Action aoReautenticar)
    {
        try
        {
            return await operacao(_cliente).ConfigureAwait(false);
        }
        catch (VaultApiException vex) when (vex.StatusCode == 403)
        {
            aoReautenticar();
            await RenovarClienteAsync().ConfigureAwait(false);
            return await operacao(_cliente).ConfigureAwait(false);
        }
    }

    public void Dispose() => _trocaDoCliente.Dispose();

    private static string ExigirEndereco(string? endereco)
    {
        if (string.IsNullOrWhiteSpace(endereco))
        {
            throw new InvalidOperationException(
                "Geo:Encryption:VaultAddress é obrigatório quando Provider = 'vault'.");
        }

        return endereco;
    }

    // EncryptionOptionsValidator já enforça exatamente um dos dois quando Provider=vault.
    // As guardas defensivas abaixo cobrem o fluxo de testes que instancia o serviço
    // diretamente (sem passar pelo validator), tornando a violação explícita em vez
    // de um NullReferenceException mais adiante em CriarCliente. Mensagens
    // separadas facilitam o diagnóstico — ambos definidos vs nenhum definido têm
    // ações corretivas distintas.
    private static bool UsaAutenticacaoKubernetes(EncryptionOptions opcoes)
    {
        bool hasRole = !string.IsNullOrWhiteSpace(opcoes.KubernetesRole);
        bool hasToken = !string.IsNullOrWhiteSpace(opcoes.VaultToken);

        ExigirNaoSerAmbos(hasRole, hasToken);
        ExigirAoMenosUm(hasRole, hasToken);
        return hasRole;
    }

    private static void ExigirNaoSerAmbos(bool hasRole, bool hasToken)
    {
        if (hasRole && hasToken)
        {
            throw new InvalidOperationException(
                "Geo:Encryption: KubernetesRole e VaultToken são mutuamente exclusivos quando Provider = 'vault'. " +
                "Em produção use KubernetesRole; em testes/dev use VaultToken. " +
                "Ver EncryptionOptionsValidator e docs/guia-config-cifragem.md.");
        }
    }

    private static void ExigirAoMenosUm(bool hasRole, bool hasToken)
    {
        if (!hasRole && !hasToken)
        {
            throw new InvalidOperationException(
                "Geo:Encryption: nem KubernetesRole nem VaultToken estão definidos quando Provider = 'vault'. " +
                "Configure GEO__ENCRYPTION__KUBERNETESROLE (produção) ou GEO__ENCRYPTION__VAULTTOKEN (testes/dev). " +
                "Ver EncryptionOptionsValidator e docs/guia-config-cifragem.md.");
        }
    }

    private VaultClient CriarCliente()
    {
        // O auth method é determinado pela configuração validada (EncryptionOptionsValidator
        // garante exatamente um entre KubernetesRole e VaultToken). Sem heurística de
        // File.Exists: se a config disser "K8s" mas o JWT não estiver disponível em disco,
        // falha-se com mensagem específica em vez de cair silenciosamente para token estático
        // (cenário que produziria NullReferenceException agora que VaultToken é mutuamente
        // exclusivo com KubernetesRole).
        IAuthMethodInfo authMethod = _usaAutenticacaoKubernetes
            ? new KubernetesAuthMethodInfo(_role, LerJwtDaContaDeServico())
            : new TokenAuthMethodInfo(_token!);

        return new VaultClient(new VaultClientSettings(_endereco, authMethod));
    }

    private string LerJwtDaContaDeServico()
    {
        string caminho = CaminhoDoJwtMontado();
        string content = File.ReadAllText(caminho);

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException(
                $"JWT do ServiceAccount em '{caminho}' está vazio. " +
                "O kubelet costuma re-popular o token automaticamente; verificar logs do pod e " +
                "o estado do volume projetado do ServiceAccount.");
        }

        return content;
    }

    private string CaminhoDoJwtMontado()
    {
        ExigirCaminhoDoJwtConfigurado();
        ExigirJwtMontado();
        return _caminhoDoJwt;
    }

    private void ExigirCaminhoDoJwtConfigurado()
    {
        if (string.IsNullOrWhiteSpace(_caminhoDoJwt))
        {
            throw new InvalidOperationException(
                "Geo:Encryption:KubernetesJwtPath está vazio. Configure o path do JWT do " +
                "ServiceAccount (default: /var/run/secrets/kubernetes.io/serviceaccount/token).");
        }
    }

    private void ExigirJwtMontado()
    {
        if (!File.Exists(_caminhoDoJwt))
        {
            throw new InvalidOperationException(
                $"JWT do ServiceAccount não encontrado em '{_caminhoDoJwt}'. " +
                "Verifique se o ServiceAccount está montado no Pod (automountServiceAccountToken=true) " +
                "e se o caminho Geo:Encryption:KubernetesJwtPath corresponde ao volume do token.");
        }
    }

    private async Task RenovarClienteAsync()
    {
        await _trocaDoCliente.WaitAsync().ConfigureAwait(false);
        try
        {
            _cliente = CriarCliente();
        }
        finally
        {
            _trocaDoCliente.Release();
        }
    }
}
