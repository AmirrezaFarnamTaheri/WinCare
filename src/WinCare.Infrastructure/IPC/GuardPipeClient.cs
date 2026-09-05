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
            ArgumentException.ThrowIfNullOrWhiteSpace(command);
            if (command.Contains('\n') || command.Contains('\r') || Encoding.UTF8.GetByteCount(command) > 4096)
                throw new ArgumentException("Expected one command of at most 4096 UTF-8 bytes.", nameof(command));
            await _lock.WaitAsync(cancellationToken);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                // The Guard serves one request per connection. Connect under the same lock
                // as the exchange so queued callers cannot reuse another caller's pipe.
                if (!IsConnected)
                {
                    ResetConnection();
                    _pipeStream = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                    await _pipeStream.ConnectAsync(2000, deadline.Token);
                }
                var payloadBytes = Encoding.UTF8.GetBytes(command + "\n");
                await _pipeStream!.WriteAsync(payloadBytes, deadline.Token);
                await _pipeStream.FlushAsync(deadline.Token);

                var response = await ReadResponseLineAsync(deadline.Token);
                if (response == null)
                {
                    ResetConnection();
                }

                return response;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return null;
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
                ResetConnection();
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

            return null; // EOF without the protocol terminator is an incomplete response.
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
