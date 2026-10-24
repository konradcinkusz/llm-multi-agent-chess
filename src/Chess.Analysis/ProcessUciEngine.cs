using System.ComponentModel;
using System.Diagnostics;

namespace Chess.Analysis;

/// <summary>
/// One Stockfish process. Calls are serialised. The process starts on first use; after a cancelled or failed
/// call it is retired, and the next call starts a fresh one.
/// </summary>
internal sealed class ProcessUciEngine(string binaryPath) : IUciEngine
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;

    public async Task<UciSearch> SearchAsync(string fen, IReadOnlyList<string> uciMoves, int depth, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var process = await StartIfNeededAsync(ct);
            try
            {
                // Cancelling a search stops the process, which ends the read below; the catch then retires it.
                using var stopOnCancel = ct.Register(() => Stop(process));

                var position = uciMoves.Count == 0 ? $"position fen {fen}" : $"position fen {fen} moves {string.Join(' ', uciMoves)}";
                await SendAsync(process, position);
                await SendAsync(process, $"go depth {depth}");

                var transcript = new List<string>();
                while (true)
                {
                    var line = await ReadLineAsync(process, ct)
                        ?? throw new InvalidOperationException("Stockfish exited during a search.");
                    transcript.Add(line);
                    if (line.StartsWith("bestmove", StringComparison.Ordinal))
                    {
                        break;
                    }
                }

                return UciTranscript.Parse(transcript);
            }
            catch
            {
                Retire(process);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_process is { } process)
            {
                Retire(process);
            }
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private async Task<Process> StartIfNeededAsync(CancellationToken ct)
    {
        if (_process is { HasExited: false } running)
        {
            return running;
        }

        var process = Process.Start(new ProcessStartInfo(binaryPath)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException($"Could not start Stockfish at '{binaryPath}'.");

        process.StandardInput.AutoFlush = true;
        // Stockfish's stderr is not used; draining it keeps the engine from blocking on a full pipe.
        _ = process.StandardError.ReadToEndAsync(CancellationToken.None);
        _process = process;

        try
        {
            await SendAsync(process, "uci");
            await ReadUntilAsync(process, "uciok", ct);
            await SendAsync(process, "isready");
            await ReadUntilAsync(process, "readyok", ct);
        }
        catch
        {
            Retire(process);
            throw;
        }

        return process;
    }

    private static async Task SendAsync(Process process, string command) => await process.StandardInput.WriteLineAsync(command);

    private static async Task ReadUntilAsync(Process process, string expected, CancellationToken ct)
    {
        while (true)
        {
            var line = await ReadLineAsync(process, ct)
                ?? throw new InvalidOperationException($"Stockfish exited before answering '{expected}'.");
            if (line.Trim() == expected)
            {
                return;
            }
        }
    }

    private static async Task<string?> ReadLineAsync(Process process, CancellationToken ct)
    {
        var line = await process.StandardOutput.ReadLineAsync(ct);
        ct.ThrowIfCancellationRequested();
        return line;
    }

    private void Retire(Process process)
    {
        Stop(process);
        process.Dispose();
        if (ReferenceEquals(_process, process))
        {
            _process = null;
        }
    }

    private static void Stop(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Already gone: nothing left to stop.
        }
    }
}
