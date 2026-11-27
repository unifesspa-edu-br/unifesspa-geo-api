namespace Unifesspa.Geo.IntegrationTests.Etl;

using Microsoft.Extensions.Logging;

/// <summary>
/// Guarda, em memória, tudo o que os componentes da carga registram: a mensagem formatada,
/// o texto completo de qualquer exceção entregue ao registro e os valores estruturados.
/// </summary>
internal sealed class RegistroDeLogs
{
    private readonly List<string> _entradas = [];

    public IReadOnlyList<string> Entradas => _entradas;

    public string TextoCompleto => string.Join('\n', _entradas);

    public ILogger<T> Para<T>() => new LoggerDoRegistro<T>(this);

    private void Adicionar(string entrada) => _entradas.Add(entrada);

    private sealed class LoggerDoRegistro<T> : ILogger<T>
    {
        private readonly RegistroDeLogs _registro;

        public LoggerDoRegistro(RegistroDeLogs registro)
        {
            _registro = registro;
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

            _registro.Adicionar(formatter(state, exception));
            _registro.Adicionar(state?.ToString() ?? string.Empty);
            if (exception is not null)
            {
                _registro.Adicionar(exception.ToString());
            }
        }
    }
}
