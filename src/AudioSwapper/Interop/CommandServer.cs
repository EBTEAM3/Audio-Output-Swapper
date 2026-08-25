using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace AudioSwapper.Interop;

/// <summary>
/// A named-pipe listener so a second launch can drive the running instance
/// instead of dying against the single-instance mutex.
///
/// This is what makes "AudioSwapper.exe --swap" work from a shortcut, a Stream
/// Deck button or a script, and it is also how the UI gets exercised in testing
/// without someone having to click the tray icon by hand.
/// </summary>
internal sealed class CommandServer : IDisposable
{
    /// <summary>Per-user, matching the single-instance mutex's scope.</summary>
    public const string PipeName = "AudioSwapper.Commands";

    private readonly Dispatcher _dispatcher;
    private readonly Action<string> _onCommand;
    private readonly CancellationTokenSource _cancellation = new();
    private bool _disposed;

    public CommandServer(Dispatcher dispatcher, Action<string> onCommand)
    {
        _dispatcher = dispatcher;
        _onCommand = onCommand;

        _ = Task.Run(ListenLoopAsync);
    }

    private async Task ListenLoopAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            try
            {
                // One connection at a time is plenty: these are single-word
                // commands fired by hand, not a request stream.
                using var server = new NamedPipeServerStream(
                    PipeName, PipeDirection.In, maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(_cancellation.Token).ConfigureAwait(false);

                using var reader = new StreamReader(server);
                string? command = await reader.ReadLineAsync(_cancellation.Token).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(command))
                {
                    // Fire and forget onto the UI thread: this loop must get
                    // straight back to accepting the next connection.
                    string trimmed = command.Trim();
                    _ = _dispatcher.BeginInvoke(() => _onCommand(trimmed));
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                // A malformed or abandoned connection should not kill the
                // listener; loop round and accept the next one.
                await Task.Delay(200).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Sends a command to an already-running instance. Returns false if nothing
    /// is listening, which is how the launcher decides to start normally.
    /// </summary>
    public static bool TrySend(string command, int timeoutMs = 1500)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(timeoutMs);

            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(command);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cancellation.Cancel();
        _cancellation.Dispose();
    }
}
