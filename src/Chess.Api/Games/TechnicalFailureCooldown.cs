using Microsoft.Extensions.Options;

namespace Chess.Api.Games;

/// <summary>
/// A pause after games that failed for a technical reason (the gateway errored on every attempt of a move, a conversation could not
/// be opened, the game crashed). The tournament and benchmark dispatch starts no new game while it holds, so a gateway that is down
/// is not asked for the whole run, one aborted game after the other. The pause is <see cref="GameRunnerOptions.TechnicalCooldown"/>
/// after the first failure and doubles with each failure in a row, up to <see cref="GameRunnerOptions.MaxTechnicalCooldown"/>; a
/// game that is played to a real end clears it. A singleton: the game runner records, the tournament service asks.
/// </summary>
public sealed class TechnicalFailureCooldown(IOptions<GameRunnerOptions> options, TimeProvider clock)
{
    private readonly Lock _gate = new();
    private int _failuresInARow;
    private DateTimeOffset _until = DateTimeOffset.MinValue;

    /// <summary>A game ended in a technical failure: the next game must wait.</summary>
    public void RecordFailure()
    {
        var limits = options.Value;
        lock (_gate)
        {
            _failuresInARow = Math.Min(_failuresInARow + 1, 30);
            var pause = TimeSpan.FromTicks(limits.TechnicalCooldown.Ticks * (1L << Math.Min(_failuresInARow - 1, 20)));
            if (pause > limits.MaxTechnicalCooldown)
            {
                pause = limits.MaxTechnicalCooldown;
            }

            _until = clock.GetUtcNow() + pause;
        }
    }

    /// <summary>A game was played to a real end: the gateway works, so the pause is over.</summary>
    public void RecordSuccess()
    {
        lock (_gate)
        {
            _failuresInARow = 0;
            _until = DateTimeOffset.MinValue;
        }
    }

    /// <summary>True while no new game should be started.</summary>
    public bool IsHolding => HoldsUntil is not null;

    /// <summary>When the pause ends, or null when there is none.</summary>
    public DateTimeOffset? HoldsUntil
    {
        get
        {
            lock (_gate)
            {
                return _until > clock.GetUtcNow() ? _until : null;
            }
        }
    }
}
