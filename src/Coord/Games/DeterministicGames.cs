namespace Coord.Games;

public sealed record GameMoveResult(bool Accepted, string Reason);

public interface ITwoPlayerGame
{
    string Id { get; }
    IReadOnlyList<string> Players { get; }
    string? CurrentPlayerId { get; }
    bool IsFinished { get; }
}

public sealed record WordDuelRules(string Category, int MinimumLength = 3, int MaximumLength = 32,
    int PointsPerWord = 1, int MaxWordsPerTurn = 1);
public sealed record WordValidationResult(bool Valid, string Reason, int Score);
public interface IWordValidator
{
    WordValidationResult Validate(string word, WordDuelRules rules, IReadOnlySet<string> usedWords);
}
public sealed class DeterministicWordValidator : IWordValidator
{
    public WordValidationResult Validate(string word, WordDuelRules rules, IReadOnlySet<string> usedWords)
    {
        var value = word.Trim().ToUpperInvariant();
        if (value.Length < rules.MinimumLength || value.Length > rules.MaximumLength)
            return new(false, "Word length is outside the configured constraints.", 0);
        if (value.Any(c => !char.IsLetter(c)))
            return new(false, "Words may contain letters only.", 0);
        if (usedWords.Contains(value))
            return new(false, "That word has already been submitted.", 0);
        return new(true, "Word accepted.", rules.PointsPerWord);
    }
}
public sealed record WordDuelState(
    string Phase, IReadOnlyList<string> Players, string? CurrentPlayerId,
    IReadOnlyDictionary<string, int> Scores, IReadOnlyList<string> Words,
    string? WinnerId, string? Result);
public sealed record WordDuelSubmission(bool Accepted, string Reason, int Points, WordDuelState State);

public sealed class WordDuelGame(IWordValidator? validator = null, WordDuelRules? rules = null) : ITwoPlayerGame
{
    private readonly IWordValidator validator = validator ?? new DeterministicWordValidator();
    private readonly WordDuelRules rules = rules ?? new WordDuelRules("general");
    private readonly List<string> players = [];
    private readonly List<string> words = [];
    private readonly Dictionary<string, int> scores = new(StringComparer.Ordinal);
    private string? current; private string? winner; private string? result; private string phase = "configuring";
    public string Id => "word-duel"; public IReadOnlyList<string> Players => players.ToArray();
    public string? CurrentPlayerId => current; public bool IsFinished => phase is "won" or "drawn";
    public WordDuelRules Rules => rules; public IReadOnlySet<string> UsedWords => words.ToHashSet(StringComparer.OrdinalIgnoreCase);
    public WordDuelState State => new(phase, players.ToArray(), current, new Dictionary<string, int>(scores),
        words.ToArray(), winner, result);
    public bool AddPlayer(string id)
    {
        if (players.Count >= 2 || players.Contains(id, StringComparer.Ordinal)) return false;
        players.Add(id); scores[id] = 0; return true;
    }
    public GameMoveResult Start()
    {
        if (players.Count != 2) return new(false, "Exactly two players are required.");
        if (phase != "configuring") return new(false, "Game has already started.");
        phase = "active"; current = players[0]; return new(true, "Game started.");
    }
    public WordDuelSubmission Submit(string playerId, string word)
    {
        if (phase != "active") return Reject("Game is not active.");
        if (playerId != current) return Reject("It is not your turn.");
        var check = validator.Validate(word, rules, UsedWords);
        if (!check.Valid) return new(false, check.Reason, 0, State);
        var normalized = word.Trim().ToUpperInvariant(); words.Add(normalized); scores[playerId] += check.Score;
        if (rules.MaxWordsPerTurn <= 1) Advance();
        return new(true, check.Reason, check.Score, State);
    }
    public GameMoveResult End(string playerId)
    {
        if (phase != "active") return new(false, "Game is not active.");
        if (!players.Contains(playerId)) return new(false, "Unknown player.");
        phase = "won"; winner = scores[players[0]] == scores[players[1]] ? null :
            scores[players[0]] > scores[players[1]] ? players[0] : players[1];
        result = winner is null ? "Draw." : "Game ended."; return new(true, result);
    }
    private void Advance() { current = players[(players.IndexOf(current!) + 1) % 2]; }
    private WordDuelSubmission Reject(string reason) => new(false, reason, 0, State);
}

public readonly record struct BoardCoordinate(int Row, int Column);
public sealed record ShipLayout(string Name, IReadOnlyList<BoardCoordinate> Cells);
public sealed record BattleshipShot(bool Accepted, string Reason, bool? Hit, bool Sunk, BattleshipState State);
public sealed record BattleshipState(string Phase, IReadOnlyList<string> Players, string? CurrentPlayerId,
    IReadOnlyDictionary<BoardCoordinate, string> VisibleShots, IReadOnlyList<ShipLayout> OwnLayout,
    string? WinnerId, string? Result);

public sealed class BattleshipGame : ITwoPlayerGame
{
    private readonly List<string> players = []; private readonly Dictionary<string, List<ShipLayout>> layouts = [];
    private readonly Dictionary<string, Dictionary<BoardCoordinate, bool>> shots = [];
    private string? current; private string? winner; private string? result; private string phase = "configuring";
    public string Id => "battleship"; public IReadOnlyList<string> Players => players.ToArray();
    public string? CurrentPlayerId => current; public bool IsFinished => phase == "won";
    // The host-safe state never selects a player, so it cannot disclose a fleet.
    public BattleshipState State => new(phase, players.ToArray(), current,
        new Dictionary<BoardCoordinate, string>(), Array.Empty<ShipLayout>(), winner, result);
    public bool AddPlayer(string id)
    {
        if (players.Count >= 2 || players.Contains(id, StringComparer.Ordinal)) return false;
        players.Add(id); return true;
    }
    public GameMoveResult SetLayout(string player, IEnumerable<ShipLayout> ships)
    {
        if (!players.Contains(player)) return new(false, "Unknown player.");
        var list = ships.ToList();
        if (list.Count == 0 || list.Any(s => s.Cells.Count == 0 ||
            s.Cells.Any(c => c.Row is < 0 or >= 10 || c.Column is < 0 or >= 10)) ||
            list.SelectMany(s => s.Cells).Distinct().Count() != list.Sum(s => s.Cells.Count))
            return new(false, "Layout must contain non-empty, in-bounds, non-overlapping ships.");
        layouts[player] = list; return new(true, "Layout accepted.");
    }
    public GameMoveResult Start()
    {
        if (players.Count != 2) return new(false, "Exactly two players are required.");
        if (players.Any(p => !layouts.ContainsKey(p))) return new(false, "Both players must set a layout.");
        phase = "active"; current = players[0]; return new(true, "Game started.");
    }
    public BattleshipShot Fire(string player, BoardCoordinate target)
    {
        if (phase != "active") return Reject("Game is not active.");
        if (player != current) return Reject("It is not your turn.");
        var opponent = players.First(p => p != player);
        if (!shots.TryGetValue(player, out var fired)) shots[player] = fired = [];
        if (fired.ContainsKey(target)) return Reject("That coordinate was already fired upon.");
        var hit = layouts[opponent].SelectMany(s => s.Cells).Contains(target);
        fired[target] = hit;
        var sunk = hit && layouts[opponent].Any(s => s.Cells.Contains(target) &&
            s.Cells.All(c => fired.TryGetValue(c, out var h) && h));
        var all = layouts[opponent].SelectMany(s => s.Cells).All(c => fired.TryGetValue(c, out var h) && h);
        if (all) { phase = "won"; winner = player; result = "Fleet defeated."; }
        else current = opponent;
        return new(true, hit ? "Hit." : "Miss.", hit, sunk, ViewFor(player));
    }
    public BattleshipState ViewFor(string player)
    {
        var own = layouts.TryGetValue(player, out var l) ? l.ToArray() : [];
        var visible = new Dictionary<BoardCoordinate, string>();
        if (shots.TryGetValue(player, out var fired))
            foreach (var shot in fired) visible[shot.Key] = shot.Value ? "hit" : "miss";
        return new(phase, players.ToArray(), current, visible, own, winner, result);
    }
    private BattleshipShot Reject(string reason) => new(false, reason, null, false, ViewFor(current ?? ""));
}

public sealed record TicTacToeState(string Phase, IReadOnlyList<string> Players, string? CurrentPlayerId,
    IReadOnlyDictionary<BoardCoordinate, string> Board, IReadOnlyDictionary<string, string> Symbols,
    string? WinnerId, string? Result);
public sealed class TicTacToeGame : ITwoPlayerGame
{
    private readonly List<string> players = []; private readonly Dictionary<BoardCoordinate, string> board = [];
    private string? current; private string? winner; private string? result; private string phase = "configuring";
    public string Id => "tic-tac-toe"; public IReadOnlyList<string> Players => players.ToArray();
    public string? CurrentPlayerId => current; public bool IsFinished => phase is "won" or "drawn";
    public TicTacToeState State => new(phase, players.ToArray(), current,
        board.ToDictionary(pair => pair.Key, pair => SymbolFor(pair.Value)), Symbols(), winner, result);
    private IReadOnlyDictionary<string, string> Symbols() => players
        .Select((player, slot) => new { player, symbol = slot == 0 ? "X" : "O" })
        .ToDictionary(x => x.player, x => x.symbol);
    private string SymbolFor(string player) => Symbols()[player];
    public bool AddPlayer(string id)
    {
        if (players.Count >= 2 || players.Contains(id, StringComparer.Ordinal)) return false;
        players.Add(id); return true;
    }
    public GameMoveResult Start() { if (players.Count != 2) return new(false, "Exactly two players are required."); phase = "active"; current = players[0]; return new(true, "Game started."); }
    public GameMoveResult Move(string player, BoardCoordinate c)
    {
        if (phase != "active") return new(false, "Game is not active."); if (player != current) return new(false, "It is not your turn.");
        if (c.Row is < 0 or > 2 || c.Column is < 0 or > 2 || board.ContainsKey(c)) return new(false, "Illegal move.");
        board[c] = player;
        var won = Enumerable.Range(0, 3).Any(i =>
            Enumerable.Range(0, 3).All(j => board.ContainsKey(new(i, j)) && board[new(i, j)] == player) ||
            Enumerable.Range(0, 3).All(j => board.ContainsKey(new(j, i)) && board[new(j, i)] == player)) ||
            Enumerable.Range(0, 3).All(i => board.ContainsKey(new(i, i)) && board[new(i, i)] == player) ||
            Enumerable.Range(0, 3).All(i => board.ContainsKey(new(i, 2 - i)) && board[new(i, 2 - i)] == player);
        if (won) { phase = "won"; winner = player; result = "Three in a row."; } else if (board.Count == 9) { phase = "drawn"; result = "Board is full."; } else current = players.First(p => p != player);
        return new(true, won ? "Won." : "Move accepted.");
    }
}

public sealed record ConnectFourState(string Phase, IReadOnlyList<string> Players, string? CurrentPlayerId,
    IReadOnlyDictionary<BoardCoordinate, string> Board, IReadOnlyDictionary<string, string> Symbols,
    string? WinnerId, string? Result);
public sealed class ConnectFourGame : ITwoPlayerGame
{
    private readonly List<string> players = []; private readonly Dictionary<BoardCoordinate, string> board = [];
    private string? current; private string? winner; private string? result; private string phase = "configuring";
    public string Id => "connect-four"; public IReadOnlyList<string> Players => players.ToArray(); public string? CurrentPlayerId => current; public bool IsFinished => phase is "won" or "drawn";
    public ConnectFourState State => new(phase, players.ToArray(), current,
        board.ToDictionary(pair => pair.Key, pair => SymbolFor(pair.Value)), Symbols(), winner, result);
    private IReadOnlyDictionary<string, string> Symbols() => players
        .Select((player, slot) => new { player, symbol = slot == 0 ? "●" : "○" })
        .ToDictionary(x => x.player, x => x.symbol);
    private string SymbolFor(string player) => Symbols()[player];
    public bool AddPlayer(string id)
    {
        if (players.Count >= 2 || players.Contains(id, StringComparer.Ordinal)) return false;
        players.Add(id); return true;
    }
    public GameMoveResult Start() { if (players.Count != 2) return new(false, "Exactly two players are required."); phase = "active"; current = players[0]; return new(true, "Game started."); }
    public GameMoveResult Drop(string player, int column)
    {
        if (phase != "active") return new(false, "Game is not active."); if (player != current) return new(false, "It is not your turn."); if (column is < 0 or > 6) return new(false, "Column is outside the board.");
        var row = Enumerable.Range(0, 6).Reverse().FirstOrDefault(r => !board.ContainsKey(new(r, column)), -1); if (row < 0) return new(false, "Column is full.");
        board[new(row, column)] = player; var won = Enumerable.Range(0, 6).SelectMany(r => Enumerable.Range(0, 7).Select(c => new BoardCoordinate(r, c))).Any(c => HasFour(c, player));
        if (won) { phase = "won"; winner = player; result = "Four in a row."; } else if (board.Count == 42) { phase = "drawn"; result = "Board is full."; } else current = players.First(p => p != player);
        return new(true, won ? "Won." : "Move accepted.");
    }
    private bool HasFour(BoardCoordinate start, string player)
    {
        foreach (var (dr, dc) in new[] { (1, 0), (0, 1), (1, 1), (1, -1) })
            if (Enumerable.Range(0, 4).All(i => board.TryGetValue(new(start.Row + dr * i, start.Column + dc * i), out var p) && p == player)) return true;
        return false;
    }
}
