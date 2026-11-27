namespace Unifesspa.Geo.IntegrationTests.TestSupport;

using Microsoft.Extensions.Logging;

/// <summary>Um registro emitido por um componente: categoria, nível, modelo da mensagem, texto final e exceção.</summary>
internal sealed record RegistroCapturado(
    string Categoria,
    LogLevel Nivel,
    string? Modelo,
    string Mensagem,
    Exception? Excecao);

/// <summary>Guarda, em memória e com o nível, tudo o que os componentes registram.</summary>
internal sealed class CapturaDeRegistros
{
    private readonly List<RegistroCapturado> _registros = [];

    public IReadOnlyList<RegistroCapturado> Registros => _registros;

    public ILogger<T> Para<T>() => new LoggerDaCaptura<T>(this);

    private sealed class LoggerDaCaptura<T> : ILogger<T>
    {
        private const string ModeloOriginal = "{OriginalFormat}";

        private readonly CapturaDeRegistros _captura;

        public LoggerDaCaptura(CapturaDeRegistros captura)
        {
            _captura = captura;
        }

        IDisposable? ILogger.BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            _captura._registros.Add(new RegistroCapturado(
                typeof(T).FullName!,
                logLevel,
                ModeloDe(state),
                formatter(state, exception),
                exception));
        }

        private static string? ModeloDe(object? state) =>
            (state as IEnumerable<KeyValuePair<string, object?>>)?
                .FirstOrDefault(par => string.Equals(par.Key, ModeloOriginal, StringComparison.Ordinal)).Value as string;
    }
}
