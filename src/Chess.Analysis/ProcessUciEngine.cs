using System.ComponentModel;
using System.Diagnostics;

namespace Chess.Analysis;

/// <summary>
/// One Stockfish process. Calls are serialised. The process starts on first use; after a cancelled or failed
/// call it is retired, and the next call starts a fresh one.
/// <para>
/// A binary that does not complete the UCI handshake within <see cref="UciTimeouts.Handshake"/>, or a search that runs past
/// <see cref="UciTimeouts.Search"/>, is killed and raises a <see cref="TimeoutException"/>; a process that cannot start or dies
/// raises the exception that says so. Such a failure makes <see cref="IsAvailable"/> false for <see cref="UciTimeouts.RetryAfter"/>,
/// and a call made in that time fails at once instead of waiting for the same timeout again.
/// </para>
/// </summary>
internal sealed class ProcessUciEngine(string binaryPath, UciTimeouts? timeouts = null, TimeProvider? time = null) : IUciEngine
{
    private readonly UciTimeouts _timeouts = (timeouts ?? UciTimeouts.Default).Validated();
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private volatile Failure? _failure;

    public bool IsAvailable => _failure is not { } failure || _time.GetUtcNow() - failure.At >= _timeouts.RetryAfter;

    public async Task EnsureReadyAsync(CancellationToken ct)
    {
        // A running process has already answered the handshake: no need to queue behind a search.
        if (_process is { HasExited: false })
        {
            return;
        }

        await _gate.WaitAsync(ct);
        try
        {
            await StartIfNeededAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<UciSearch> SearchAsync(string fen, IReadOnlyList<string> uciMoves, int depth, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var process = await StartIfNeededAsync(ct);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(_timeouts.Search);
            try
            {
                // Cancelling or timing out a search stops the process, which ends the read below; the catch then retires it.
                using var stopOnCancel = deadline.Token.Register(() => Stop(process));

                var position = uciMoves.Count == 0 ? $"position fen {fen}" : $"position fen {fen} moves {string.Join(' ', uciMoves)}";
                await SendAsync(process, position, "bestmove", deadline.Token);
                await SendAsync(process, $"go depth {depth}", "bestmove", deadline.Token);

                var transcript = new List<string>();
                while (true)
                {
                    var line = await ReadLineAsync(process, deadline.Token)
                        ?? throw new InvalidOperationException("Stockfish exited during a search.");
                    transcript.Add(line);
                    if (line.StartsWith("bestmove", StringComparison.Ordinal))
                    {
                        break;
                    }
                }

                var search = UciTranscript.Parse(transcript);
                _failure = null;
                return search;
            }
            catch (Exception ex) when (deadline.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                Retire(process);
                var timeout = new TimeoutException(
                    $"Stockfish did not finish a depth-{depth} search within {_timeouts.Search.TotalSeconds:0.#} s; the process was stopped.", ex);
                Record(timeout);
                throw timeout;
            }
            catch (Exception ex)
            {
                Retire(process);
                if (ex is not (OperationCanceledException or FormatException))
                {
                    Record(ex);
                }

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

        if (_failure is { } failure && _time.GetUtcNow() - failure.At < _timeouts.RetryAfter)
        {
            throw new InvalidOperationException($"Stockfish is unavailable: {failure.Message}");
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(_timeouts.Handshake);
        Process? process = null;
        try
        {
            process = Process.Start(new ProcessStartInfo(binaryPath)
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

            // A hung or non-UCI binary never answers: the deadline kills it, which ends the read.
            var started = process;
            using var stopOnTimeout = deadline.Token.Register(() => Stop(started));
            await SendAsync(process, "uci", "uciok", deadline.Token);
            await ReadUntilAsync(process, "uciok", deadline.Token);
            await SendAsync(process, "isready", "readyok", deadline.Token);
            await ReadUntilAsync(process, "readyok", deadline.Token);
        }
        catch (Exception ex) when (deadline.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            Retire(process);
            var timeout = new TimeoutException(
                $"'{binaryPath}' did not answer the UCI handshake within {_timeouts.Handshake.TotalSeconds:0.#} s; is it a UCI engine?", ex);
            Record(timeout);
            throw timeout;
        }
        catch (Exception ex)
        {
            Retire(process);
            if (ex is not OperationCanceledException)
            {
                Record(ex);
            }

            throw;
        }

        _failure = null;
        return process;
    }

    private void Record(Exception ex) => _failure = new Failure(ex.Message, _time.GetUtcNow());

    private static async Task SendAsync(Process process, string command, string awaiting, CancellationToken ct)
    {
        try
        {
            await process.StandardInput.WriteLineAsync(command.AsMemory(), ct);
        }
        catch (Exception ex) when (ex is System.IO.IOException or ObjectDisposedException)
        {
            // The engine already exited, so its stdin is a broken pipe: report that as the engine's failure, as a failed read does.
            throw new InvalidOperationException($"Stockfish exited before answering '{awaiting}' (could not send '{command}').", ex);
        }
    }

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

    private void Retire(Process? process)
    {
        if (process is null)
        {
            return;
        }

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
            // Already gone or already disposed: nothing left to stop.
        }
    }

    private sealed record Failure(string Message, DateTimeOffset At);
}
