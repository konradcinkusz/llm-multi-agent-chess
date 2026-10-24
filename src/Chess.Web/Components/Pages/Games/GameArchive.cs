namespace Chess.Web.Components.Pages.Games;

/// <summary>Paging of the game archive. The API caps a page at 200 (03 §5); the list asks for less.</summary>
public static class GameArchive
{
    public const int PageSize = 20;

    /// <summary>The number of pages; an empty archive still has one page to show.</summary>
    public static int PageCount(int total) => Math.Max(1, (total + PageSize - 1) / PageSize);
}
