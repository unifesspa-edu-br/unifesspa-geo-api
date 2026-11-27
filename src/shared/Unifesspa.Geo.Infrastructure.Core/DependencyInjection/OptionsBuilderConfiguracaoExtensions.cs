namespace Unifesspa.Geo.Infrastructure.Core.DependencyInjection;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

internal static class OptionsBuilderConfiguracaoExtensions
{
    /// <summary>
    /// Vincula a seção da configuração, se ela existir, e só depois aplica o ajuste feito em código,
    /// que por isso prevalece sobre o valor lido do arquivo.
    /// </summary>
    internal static OptionsBuilder<T> ComConfiguracao<T>(
        this OptionsBuilder<T> builder,
        IConfiguration? configuration,
        string secao,
        Action<T>? ajuste)
        where T : class
    {
        IConfigurationSection? section = configuration?.GetSection(secao);
        if (section?.Exists() == true)
        {
            builder.Bind(section);
        }

        if (ajuste is not null)
        {
            builder.Configure(ajuste);
        }

        return builder;
    }
}
