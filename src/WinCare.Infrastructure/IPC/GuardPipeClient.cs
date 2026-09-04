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
        private const int MaxResponseBytes = 64 * 1024;
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
            catch (OperationCanceledException)
            {
                ResetConnection();
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GuardPipeClient] Connection failed: {ex.Message}");
                ResetConnection();
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

                var response = await ReadResponseLineAsync(cancellationToken);
                if (response == null)
                {
                    ResetConnection();
                }

                return response;
            }
            catch (OperationCanceledException)
            {
                ResetConnection();
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GuardPipeClient] SendCommand error: {ex.Message}");
                ResetConnection();
                return null;
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Reads a single newline-terminated response line directly off the pipe with a hard
        /// byte cap. Reading the raw stream (instead of wrapping a fresh <see cref="StreamReader"/>
        /// per request) avoids a reader buffering past the first line and silently discarding
        /// the remainder, and bounds memory against an oversized or non-terminating peer.
        /// </summary>
        private async Task<string?> ReadResponseLineAsync(CancellationToken cancellationToken)
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[256];
            while (true)
            {
                int read = await _pipeStream!.ReadAsync(chunk, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                for (int i = 0; i < read; i++)
                {
                    byte b = chunk[i];
                    if (b == (byte)'\n')
                    {
                        return Encoding.UTF8.GetString(buffer.ToArray()).TrimEnd('\r');
                    }
                    buffer.WriteByte(b);
                    if (buffer.Length > MaxResponseBytes)
                    {
                        throw new IOException($"Guard response exceeded the {MaxResponseBytes}-byte limit.");
                    }
                }
            }

            return buffer.Length > 0 ? Encoding.UTF8.GetString(buffer.ToArray()).TrimEnd('\r') : null;
        }

        private void ResetConnection()
        {
            if (_pipeStream == null) return;
            try { _pipeStream.Dispose(); } catch { }
            _pipeStream = null;
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
