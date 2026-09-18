namespace Coord.Games;

public enum WhoAmIPhase { Configuring, Active, Paused, Won, Aborted }
public enum WhoAmIAnswer { Yes, No, Uncertain, Rephrase }

public sealed record Identity(string Name, IReadOnlyList<string> Aliases);
public interface IIdentityProvider
{
    Task<Identity> ChooseAsync(string category, CancellationToken cancellationToken = default);
}

public sealed class ManualIdentityProvider(Identity identity) : IIdentityProvider
{
    public Task<Identity> ChooseAsync(string category, CancellationToken cancellationToken = default) =>
        Task.FromResult(identity);
}

public sealed class DeterministicIdentityProvider(IReadOnlyDictionary<string, Identity> identities) : IIdentityProvider
{
    public Task<Identity> ChooseAsync(string category, CancellationToken cancellationToken = default) =>
        Task.FromResult(identities.TryGetValue(category, out var identity)
            ? identity
            : throw new InvalidOperationException($"No identity configured for category '{category}'."));
}

public sealed record WhoAmIHistoryEntry(string PlayerId, string Text, string Kind, string? Response);
public sealed record WhoAmIPublicState(
    WhoAmIPhase Phase, string? Category, IReadOnlyList<string> Players, string? CurrentPlayerId,
    IReadOnlyList<WhoAmIHistoryEntry> History, string? WinnerId, string? Result);
public sealed record WhoAmIAction(bool Accepted, string Reason, WhoAmIPublicState State);

public sealed class WhoAmIGame(IIdentityProvider provider)
{
    private readonly object sync = new();
    private readonly List<string> players = [];
    private readonly List<WhoAmIHistoryEntry> history = [];
    private Identity? identity;
    private string? category;
    private int turn;
    private string? winner;
    private string? result;
    private WhoAmIPhase phase = WhoAmIPhase.Configuring;

    public WhoAmIPublicState State { get { lock (sync) return Snapshot(); } }
    public Identity? SelectedIdentity { get { lock (sync) return identity; } }
    public void AddPlayer(string playerId)
    {
        lock (sync) if (!players.Contains(playerId, StringComparer.Ordinal)) players.Add(playerId);
    }
    public void RemovePlayer(string playerId)
    {
        lock (sync)
        {
            players.Remove(playerId);
            if (phase == WhoAmIPhase.Active && players.Count == 0) Abort("All players disconnected.");
            else if (phase == WhoAmIPhase.Active && CurrentPlayer() == playerId) AdvanceTurn();
        }
    }
    public WhoAmIAction Configure(string category)
    {
        lock (sync)
        {
            if (phase != WhoAmIPhase.Configuring) return Reject("Game is already started.");
            if (string.IsNullOrWhiteSpace(category)) return Reject("Category is required.");
            this.category = category.Trim();
            return Accept("Category configured.");
        }
    }
    public async Task<WhoAmIAction> StartAsync(CancellationToken cancellationToken = default)
    {
        string selectedCategory;
        lock (sync)
        {
            if (phase != WhoAmIPhase.Configuring) return Reject("Game is not configuring.");
            if (players.Count == 0) return Reject("At least one player is required.");
            if (string.IsNullOrWhiteSpace(category)) return Reject("Category is required.");
            selectedCategory = category;
        }
        var selected = await provider.ChooseAsync(selectedCategory, cancellationToken);
        lock (sync)
        {
            if (phase != WhoAmIPhase.Configuring) return Reject("Game is not configuring.");
            identity = selected;
            phase = WhoAmIPhase.Active;
            turn = 0;
            return Accept("Game started.");
        }
    }
    public WhoAmIAction SubmitQuestion(string playerId, string question)
    {
        lock (sync)
        {
            if (!CanAct(playerId, out var failure)) return Reject(failure);
            if (string.IsNullOrWhiteSpace(question) || question.Length > 160 || !question.TrimEnd().EndsWith('?'))
                return Reject("Questions must be concise and end with '?'.");
            history.Add(new(playerId, question.Trim(), "question", null));
            return Accept("Question submitted.");
        }
    }
    public WhoAmIAction AnswerQuestion(string playerId, WhoAmIAnswer answer)
    {
        lock (sync)
        {
            if (phase != WhoAmIPhase.Active) return Reject("Game is not active.");
            var index = history.FindLastIndex(h => h.PlayerId == playerId && h.Kind == "question" && h.Response is null);
            if (index < 0) return Reject("No unanswered question.");
            if (answer == WhoAmIAnswer.Rephrase) return Accept("Please rephrase the question.");
            history[index] = history[index] with { Response = answer.ToString() };
            if (answer != WhoAmIAnswer.Uncertain) AdvanceTurn();
            return Accept(answer == WhoAmIAnswer.Uncertain ? "Uncertain; turn retained." : "Answer recorded.");
        }
    }
    public WhoAmIAction Guess(string playerId, string guess)
    {
        lock (sync)
        {
            if (!CanAct(playerId, out var failure)) return Reject(failure);
            if (string.IsNullOrWhiteSpace(guess)) return Reject("Guess is required.");
            var correct = string.Equals(guess.Trim(), identity!.Name, StringComparison.OrdinalIgnoreCase)
                || identity.Aliases.Any(a => string.Equals(guess.Trim(), a, StringComparison.OrdinalIgnoreCase));
            history.Add(new(playerId, guess.Trim(), "guess", correct ? "Correct" : "Incorrect"));
            if (correct) { phase = WhoAmIPhase.Won; winner = playerId; result = "Correct guess."; }
            else AdvanceTurn();
            return Accept(correct ? "Correct guess." : "Incorrect guess; turn lost.");
        }
    }
    public WhoAmIAction Skip(string playerId)
    {
        lock (sync)
        {
            if (!CanAct(playerId, out var failure)) return Reject(failure);
            AdvanceTurn();
            return Accept("Turn skipped.");
        }
    }
    public WhoAmIAction Pause()
    {
        lock (sync) { if (phase != WhoAmIPhase.Active) return Reject("Game is not active."); phase = WhoAmIPhase.Paused; return Accept("Game paused."); }
    }
    public WhoAmIAction Resume()
    {
        lock (sync) { if (phase != WhoAmIPhase.Paused) return Reject("Game is not paused."); phase = WhoAmIPhase.Active; return Accept("Game resumed."); }
    }

    private bool CanAct(string playerId, out string failure)
    {
        if (phase != WhoAmIPhase.Active) { failure = "Game is not active."; return false; }
        if (CurrentPlayer() != playerId) { failure = "It is not your turn."; return false; }
        failure = ""; return true;
    }
    private string? CurrentPlayer() => players.Count == 0 ? null : players[turn % players.Count];
    private void AdvanceTurn() { if (players.Count > 0) turn = (turn + 1) % players.Count; }
    private void Abort(string why) { phase = WhoAmIPhase.Aborted; result = why; }
    private WhoAmIAction Accept(string reason) => new(true, reason, Snapshot());
    private WhoAmIAction Reject(string reason) => new(false, reason, Snapshot());
    private WhoAmIPublicState Snapshot() => new(phase, category, players.ToArray(), CurrentPlayer(), history.ToArray(), winner, result);
}
