using System.Text.Json;
using Coord.Protocol;

namespace Coord.Application;

/// <summary>Pure, privacy-safe formatting for the generic game state messages.</summary>
public static class GameUiRenderer
{
    public static string Render(GameStateMessage? whoAmI, GameActionMessage? generic,
        GamePrivateStateMessage? privateState = null)
    {
        if (whoAmI is not null && whoAmI.GameId == "who-am-i")
            return RenderWhoAmI(whoAmI);
        if (generic is null) return "Waiting for the host to start an activity.";
        return generic.GameId switch
        {
            "tic-tac-toe" => RenderGrid(generic.Payload, 3, 3, "Tic-Tac-Toe"),
            "connect-four" => RenderGrid(generic.Payload, 6, 7, "Connect Four"),
            "battleship" => RenderBattleship(generic.Payload, privateState?.State),
            "word-duel" => RenderWordDuel(generic.Payload),
            "solo-mystery" => RenderMystery(generic.Payload),
            _ => RenderObject(generic.Payload)
        };
    }

    public static string RenderHistory(IReadOnlyList<GameHistoryItem> history) =>
        history.Count == 0 ? "History: (none)" :
        "History:\n" + string.Join("\n", history.TakeLast(10).Select(h =>
            $"  {h.PlayerId}: {h.Text} [{h.Response ?? "pending"}]"));

    private static string RenderWhoAmI(GameStateMessage state) =>
        $"Who Am I?  phase: {state.Phase}  turn: {state.CurrentPlayerId ?? "-"}\n" +
        RenderHistory(state.History) +
        (state.Result is null ? "" : $"\nResult: {state.Result}");

    private static string RenderGrid(JsonElement state, int rows, int columns, string title) =>
        RenderGrid(ReadBoard(state), state, rows, columns, title);

    private static string RenderGrid(Dictionary<(int Row, int Column), string> board,
        JsonElement state, int rows, int columns, string title)
    {
        var lines = new List<string> { $"{title}  phase: {Read(state, "phase")}  turn: {Read(state, "currentPlayerId") ?? "-"}" };
        if (state.TryGetProperty("symbols", out var symbols) && symbols.ValueKind == JsonValueKind.Object)
            lines.Add("Players: " + string.Join("  ", symbols.EnumerateObject().Select(p => $"{p.Name}={p.Value}")));
        lines.Add("      " + string.Join("   ", Enumerable.Range(0, columns).Select(c => ((char)('A' + c)).ToString())));
        var border = "    +" + string.Join("+", Enumerable.Repeat("---", columns)) + "+";
        lines.Add(border);
        for (var r = 0; r < rows; r++)
            lines.Add($" {r + 1,2} |" + string.Join("|", Enumerable.Range(0, columns)
                .Select(c => $" {Cell(board, r, c)} ")) + "|");
        lines.Add(border);
        var result = Read(state, "result");
        if (result is not null) lines.Add($"Result: {result}");
        return string.Join("\n", lines);
    }

    private static string RenderBattleship(JsonElement publicState, JsonElement? privateState)
    {
        var state = privateState ?? publicState;
        var board = ReadBoard(state);
        if (state.TryGetProperty("ownLayout", out var layout) && layout.ValueKind == JsonValueKind.Array)
            foreach (var ship in layout.EnumerateArray())
                if (ship.TryGetProperty("cells", out var cells))
                    foreach (var cell in cells.EnumerateArray())
                        if (cell.TryGetProperty("row", out var row) && cell.TryGetProperty("column", out var col))
                            board[(row.GetInt32(), col.GetInt32())] = "#";
        if (state.TryGetProperty("visibleShots", out var shots) && shots.ValueKind == JsonValueKind.Object)
            foreach (var shot in shots.EnumerateObject())
            {
                var parts = shot.Name.Trim('(', ')').Split(',', StringSplitOptions.TrimEntries);
                if (parts.Length == 2 && int.TryParse(parts[0], out var row) && int.TryParse(parts[1], out var col))
                    board[(row, col)] = shot.Value.GetString() == "hit" ? "X" : "o";
            }
        var text = RenderGrid(board, state, 10, 10, "Battleship (your fleet / shots)");
        return text + "\nOpponent ships are hidden; X=hit, o=miss, #=your ship.";
    }

    private static string RenderWordDuel(JsonElement state)
    {
        var scores = state.TryGetProperty("scores", out var score) && score.ValueKind == JsonValueKind.Object
            ? string.Join("  ", score.EnumerateObject().Select(p => $"{p.Name}: {p.Value.GetInt32()}")) : "(none)";
        var words = state.TryGetProperty("words", out var list) && list.ValueKind == JsonValueKind.Array
            ? string.Join(", ", list.EnumerateArray().Select(x => x.GetString())) : "(none)";
        return $"Word Duel  phase: {Read(state, "phase")}  turn: {Read(state, "currentPlayerId") ?? "-"}\n" +
            $"Scores: {scores}\nWords: {words}\n{Result(state)}";
    }

    private static string RenderMystery(JsonElement state) =>
        $"Station of Echoes  phase: {Read(state, "phase")}\n" +
        $"Scene: {Read(state, "scene") ?? "(waiting)"}\nClue: {Read(state, "clue") ?? "(none)"}\n" +
        $"Actions: {ReadArray(state, "history")}\n{Result(state)}";

    private static string RenderObject(JsonElement state) => state.ValueKind == JsonValueKind.Object
        ? string.Join("\n", state.EnumerateObject().Select(p => $"{p.Name}: {p.Value}"))
        : state.ToString();

    private static string Result(JsonElement state) => Read(state, "result") is { } result ? $"Result: {result}" : "";
    private static string? Read(JsonElement state, string name) =>
        state.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : null;
    private static string ReadArray(JsonElement state, string name) =>
        state.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? string.Join(" | ", value.EnumerateArray().Select(x => x.ToString())) : "(none)";
    private static Dictionary<(int Row, int Column), string> ReadBoard(JsonElement state)
    {
        var result = new Dictionary<(int, int), string>();
        if (!state.TryGetProperty("board", out var board) || board.ValueKind != JsonValueKind.Object) return result;
        foreach (var item in board.EnumerateObject())
        {
            var parts = item.Name.Trim('(', ')').Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && int.TryParse(parts[0], out var row) && int.TryParse(parts[1], out var col))
                result[(row, col)] = item.Value.ToString();
        }
        return result;
    }
    private static string Cell(Dictionary<(int Row, int Column), string> board, int row, int column) =>
        board.TryGetValue((row, column), out var value) ? Mark(value) : " ";
    private static string Mark(string value) => value.Length == 0 ? " " : value[..Math.Min(1, value.Length)].ToUpperInvariant();
}
