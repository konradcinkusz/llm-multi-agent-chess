using System.Globalization;
using Chess.Web.Api;

namespace Chess.Web.Components.Pages.Broadcast;

/// <summary>
/// What the OBS page shows, as plain functions of the data it has: the move tail, the start times, the countdown and the
/// model colours. No state and no I/O, so the rules can be tested without rendering.
/// </summary>
public static class LiveBroadcastView
{
    /// <summary>How many full moves the move tail shows.</summary>
    public const int MoveTailRows = 6;

    /// <summary>The categorical colours of 03 §6.1 for a dark background (the default theme).</summary>
    public static readonly string[] DarkSeries = ["#3987e5", "#d95926", "#199e70", "#c98500", "#d55181", "#008300", "#9085e9", "#e66767"];

    /// <summary>The categorical colours of 03 §6.1 for a light background (<c>?theme=light</c>).</summary>
    public static readonly string[] LightSeries = ["#2a78d6", "#eb6834", "#1baf7a", "#eda100", "#e87ba4", "#008300", "#6250d6", "#e34948"];

    /// <summary>
    /// The countdown of the intermission card: "mm:ss" under an hour, "h:mm:ss" from an hour on, and "a moment" once the
    /// start time has come but the runner has not started the game yet.
    /// </summary>
    public static string CountdownText(TimeSpan left)
    {
        if (left <= TimeSpan.Zero)
        {
            return "a moment";
        }

        // Whole seconds, rounded up, so the card never reads 00:00 before the start.
        left = TimeSpan.FromSeconds(Math.Ceiling(left.TotalSeconds));
        return left.TotalHours >= 1
            ? ((int)left.TotalHours).ToString(CultureInfo.InvariantCulture) + left.ToString(@"\:mm\:ss", CultureInfo.InvariantCulture)
            : left.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }

    /// <summary>Local start time of a slot, as "19:30".</summary>
    public static string StartLabel(DateTimeOffset plannedStart) =>
        plannedStart.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// The last <see cref="MoveTailRows"/> full moves, White and Black side by side. Black is null while it is still to move.
    /// Sides come from the ply (ply 1 is White's first move), because the server writes colours in lower case.
    /// </summary>
    public static IReadOnlyList<MoveRow> MoveTail(IReadOnlyList<MoveDto> moves) =>
        moves
            .GroupBy(move => (move.Ply + 1) / 2)
            .OrderBy(group => group.Key)
            .TakeLast(MoveTailRows)
            .Select(group => new MoveRow(
                group.Key,
                group.FirstOrDefault(move => move.Ply % 2 == 1)?.San,
                group.FirstOrDefault(move => move.Ply % 2 == 0)?.San))
            .ToList();

    /// <summary>
    /// The colour of a model. Models take their slot from their place in the alphabetical list of all models, so a
    /// model keeps its colour on every game. A model not in the list gets a slot from its characters, which is also stable.
    /// </summary>
    public static string Colour(string modelId, IReadOnlyList<string> sortedModelIds, bool light)
    {
        var index = sortedModelIds.ToList().IndexOf(modelId);
        var slot = index >= 0 ? index : modelId.Sum(c => (int)c);
        var palette = light ? LightSeries : DarkSeries;
        return palette[slot % palette.Length];
    }
}

/// <summary>One line of the move tail: the move number and each side's move.</summary>
public sealed record MoveRow(int Number, string? White, string? Black);
