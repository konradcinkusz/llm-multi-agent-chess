namespace ChessApi.Entities;

public class MoveRecord
{
    public int Id { get; set; }
    public Guid GameId { get; set; }
    public Game Game { get; set; } = null!;

    public int MoveNumber { get; set; }   // chess pair number (1, 1, 2, 2, …)
    public int Ply { get; set; }          // absolute 1-based index
    public string Color { get; set; } = ""; // "White" | "Black"
    public string San { get; set; } = "";   // Standard Algebraic Notation

    public float Evaluation { get; set; }
    public int MaterialAdvantage { get; set; }
    public bool IsCheck { get; set; }
    public bool IsCheckmate { get; set; }

    public DateTime PlayedAt { get; set; }
}
