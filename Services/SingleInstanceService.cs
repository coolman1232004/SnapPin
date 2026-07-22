using System.IO;
using System.IO.Pipes;
using SnapAnchor.Models;

namespace SnapAnchor.Services;

internal sealed class SingleInstanceService : IDisposable
{
    private readonly string _pipeName;
    private readonly Mutex? _mutex;
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _listener;

    public SingleInstanceService(string applicationId)
    {
        var safeId = string.Concat(applicationId.Select(character => char.IsLetterOrDigit(character) ? character : '_'));
        var safeUser = string.Concat(Environment.UserName.Select(character => char.IsLetterOrDigit(character) ? character : '_'));
        _pipeName = $"{safeId}_{safeUser}";
        _mutex = new Mutex(initiallyOwned: true, $"Local\\{safeId}", out var createdNew);
        IsPrimaryInstance = createdNew;
        if (!createdNew)
        {
            _mutex.Dispose();
            _mutex = null;
        }
    }

    public bool IsPrimaryInstance { get; }
    public event EventHandler? ActivationRequested;
    public event EventHandler<AppCommandEventArgs>? CommandReceived;

    public void StartListening()
    {
        if (!IsPrimaryInstance || _listener is not null) return;
        _listener = Task.Run(ListenAsync);
    }

    public void SignalPrimaryInstance() => SignalPrimaryInstance(AppCommand.Activate);

    public void SignalPrimaryInstance(AppCommand command)
    {
        if (IsPrimaryInstance) return;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
                client.Connect(250);
                using var writer = new StreamWriter(client) { AutoFlush = true };
                writer.WriteLine(command.Serialize());
                return;
            }
            catch (IOException) { Thread.Sleep(100); }
            catch (TimeoutException) { Thread.Sleep(100); }
        }
    }

    private async Task ListenAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(_cancellation.Token);
                using var reader = new StreamReader(server);
                var message = await reader.ReadLineAsync(_cancellation.Token);
                if (!AppCommand.TryDeserialize(message, out var command)) continue;
                if (command.Kind == AppCommandKind.Activate) ActivationRequested?.Invoke(this, EventArgs.Empty);
                CommandReceived?.Invoke(this, new AppCommandEventArgs(command));
            }
            catch (OperationCanceledException) { break; }
            catch (IOException) when (!_cancellation.IsCancellationRequested) { }
        }
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        if (_mutex is not null)
        {
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
            _mutex.Dispose();
        }
        _cancellation.Dispose();
    }
}
