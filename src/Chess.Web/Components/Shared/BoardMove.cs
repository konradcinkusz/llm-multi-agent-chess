namespace Chess.Web.Components.Shared;

/// <summary>The squares of a move to highlight on the board, e.g. ("e2", "e4").</summary>
public sealed record BoardMove(string From, string To);
