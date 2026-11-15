using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PowerLessToy.NotifyPosition.Core;

public class IpcServer : IDisposable
{
    private readonly Func<string, string> _commandHandler;
    private CancellationTokenSource? _cts;
    private Task? _serverTask;
    private bool _disposed;

    public IpcServer(Func<string, string> commandHandler)
    {
        _commandHandler = commandHandler ?? throw new ArgumentNullException(nameof(commandHandler));
    }

    public void Start()
    {
        if (_serverTask != null) return;
        _cts = new CancellationTokenSource();
        _serverTask = Task.Run(() => ServerLoop(_cts.Token));
        NotifyPositionLogger.Info($"IPC Server started on pipe '{IpcConstants.PipeName}'", nameof(IpcServer));
    }

    private async Task ServerLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(
                    IpcConstants.PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous
                );

                await server.WaitForConnectionAsync(token);

                _ = Task.Run(async () =>
                {
                    using (server)
                    {
                        try
                        {
                            using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
                            using var writer = new StreamWriter(server, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

                            string? line = await reader.ReadLineAsync();
                            if (!string.IsNullOrEmpty(line))
                            {
                                NotifyPositionLogger.Debug($"IPC Server received command: '{line}'", nameof(IpcServer));
                                string response = _commandHandler(line.Trim());
                                await writer.WriteLineAsync(response);
                            }
                        }
                        catch (Exception ex)
                        {
                            NotifyPositionLogger.Error("Error processing IPC client connection", ex, nameof(IpcServer));
                        }
                    }
                }, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                NotifyPositionLogger.Error("IPC Server loop error", ex, nameof(IpcServer));
                try { await Task.Delay(1000, token); } catch { break; }
            }
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _cts?.Dispose();
    }
}
