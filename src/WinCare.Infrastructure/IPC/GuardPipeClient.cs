using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WinCare.Infrastructure.IPC
{
    public sealed class GuardPipeClient : IAsyncDisposable
    {
        private const string PipeName = "WinCareGuardIPC";
        private NamedPipeClientStream? _pipeStream;
        private readonly SemaphoreSlim _lock = new(1, 1);

        public bool IsConnected => _pipeStream?.IsConnected ?? false;

        public async Task<bool> TryConnectAsync(int timeoutMs = 2000, CancellationToken cancellationToken = default)
        {
            await _lock.WaitAsync(cancellationToken);
            try
            {
                if (IsConnected) return true;

                _pipeStream = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await _pipeStream.ConnectAsync(timeoutMs, cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GuardPipeClient] Connection failed: {ex.Message}");
                if (_pipeStream != null)
                {
                    try { _pipeStream.Dispose(); } catch { }
                    _pipeStream = null;
                }
                return false;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<string?> SendCommandAsync(string command, CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
            {
                var connected = await TryConnectAsync(2000, cancellationToken);
                if (!connected || _pipeStream == null) return null;
            }

            await _lock.WaitAsync(cancellationToken);
            try
            {
                var payloadBytes = Encoding.UTF8.GetBytes(command + "\n");
                await _pipeStream!.WriteAsync(payloadBytes, cancellationToken);
                await _pipeStream.FlushAsync(cancellationToken);

                using var reader = new StreamReader(_pipeStream, Encoding.UTF8, leaveOpen: true);
                return await reader.ReadLineAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GuardPipeClient] SendCommand error: {ex.Message}");
                if (_pipeStream != null)
                {
                    try { _pipeStream.Dispose(); } catch { }
                    _pipeStream = null;
                }
                return null;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _lock.WaitAsync();
            try
            {
                if (_pipeStream != null)
                {
                    await _pipeStream.DisposeAsync();
                    _pipeStream = null;
                }
            }
            finally
            {
                _lock.Release();
                _lock.Dispose();
            }
        }
    }
}
