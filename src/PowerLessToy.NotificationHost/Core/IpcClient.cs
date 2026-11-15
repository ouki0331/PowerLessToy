using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PowerLessToy.NotifyPosition.Core;

public static class IpcClient
{
    public static bool SendCommand(string command, out string response, int timeoutMs = 2500)
    {
        response = string.Empty;
        try
        {
            using var client = new NamedPipeClientStream(".", IpcConstants.PipeName, PipeDirection.InOut);
            client.Connect(timeoutMs);

            using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);
            using var writer = new StreamWriter(client, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

            writer.WriteLine(command);
            response = reader.ReadLine() ?? string.Empty;
            return true;
        }
        catch (TimeoutException)
        {
            NotifyPositionLogger.Debug($"IPC Client connect timed out for command '{command}'", nameof(IpcClient));
            return false;
        }
        catch (Exception ex)
        {
            NotifyPositionLogger.Debug($"IPC Client command '{command}' failed: {ex.Message}", nameof(IpcClient));
            return false;
        }
    }

    public static async Task<(bool success, string response)> SendCommandAsync(string command, int timeoutMs = 2500, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", IpcConstants.PipeName, PipeDirection.InOut);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeoutMs);

            await client.ConnectAsync(cts.Token);

            using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);
            using var writer = new StreamWriter(client, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

            await writer.WriteLineAsync(command);
            string response = await reader.ReadLineAsync(cts.Token) ?? string.Empty;
            return (true, response);
        }
        catch (Exception ex)
        {
            NotifyPositionLogger.Debug($"IPC Client async command '{command}' failed: {ex.Message}", nameof(IpcClient));
            return (false, string.Empty);
        }
    }

    public static bool IsHostRunning()
    {
        return SendCommand(IpcConstants.CmdPing, out string resp, 800) && resp.Trim() == IpcConstants.RespPong;
    }
}
