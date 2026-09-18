using Coord.Config;
using Coord.Protocol;

namespace Coord.Client;

public sealed class CoordClient(CoordConfig config)
{
    public CoordConfig Config { get; } = config;
    public LobbyState? Lobby { get; private set; }
    public AdmissionResponse? Admission { get; private set; }
    public GameStateMessage? Game { get; private set; }
    public GamePrivateStateMessage? PrivateGame { get; private set; }
    public string? SelectedGame { get; private set; }
    public bool IsWaitingForGame => SelectedGame is null;
    public GameActionMessage? GenericGameState { get; private set; }
    private JsonLineConnection? connection;

    public async Task RunAsync(string name, string roomCode, CancellationToken cancellationToken)
    {
        var (host, port) = ConfigLoader.ParseAddress(Config.Network.Address);
        connection = await TcpLineConnection.ConnectAsync(
            host, port, Config.Network.ConnectTimeout, cancellationToken, Config.Network.UseTls);
        try
        {
            await connection.SendAsync(new HelloMessage(name, ProtocolConstants.CurrentVersion), cancellationToken);
            await connection.SendAsync(new AdmissionRequest(roomCode, name), cancellationToken);
            await foreach (var message in connection.ReadAllAsync(cancellationToken))
            {
                switch (message)
                {
                    case AdmissionResponse admission: Admission = admission; break;
                    case LobbyState lobby: Lobby = lobby; break;
                    case PlayerStatus status when Lobby is not null:
                        Lobby = Lobby with
                        {
                            Players = Lobby.Players.Where(p => p.Id != status.PlayerId)
                                .Append(new PlayerInfo(status.PlayerId, status.Name, status.Connected, status.Ready)).ToArray()
                        };
                        break;
                    case GameStateMessage game: Game = game; break;
                    case GamePrivateStateMessage privateGame: PrivateGame = privateGame; break;
                    case GameSelectionMessage selection: SelectedGame = selection.GameId; break;
                    case GameActionMessage action when action.Action.Equals("state", StringComparison.OrdinalIgnoreCase):
                        GenericGameState = action;
                        SelectedGame = action.GameId;
                        break;
                }
                if (Admission is { Accepted: false }) break;
            }

        }
        finally
        {
            await connection.CloseAsync("client closed");
            await connection.DisposeAsync();
            connection = null;
        }
    }

    public Task ConfigureGameAsync(string category, CancellationToken cancellationToken = default) =>
        SendAsync(new GameSetupMessage(category), cancellationToken);
    public Task StartGameAsync(string category = "", CancellationToken cancellationToken = default) =>
        SendAsync(new GameStartMessage(category), cancellationToken);
    public Task AskAsync(string question, CancellationToken cancellationToken = default) =>
        SendAsync(new GameQuestionMessage(Admission?.PlayerId ?? "", question), cancellationToken);
    public Task AnswerAsync(string answer, CancellationToken cancellationToken = default) =>
        SendAsync(new GameAnswerMessage(Admission?.PlayerId ?? "", answer), cancellationToken);
    public Task GuessAsync(string guess, CancellationToken cancellationToken = default) =>
        SendAsync(new GameGuessMessage(Admission?.PlayerId ?? "", guess), cancellationToken);
    public Task ControlGameAsync(string action, CancellationToken cancellationToken = default) =>
        SendAsync(new GameControlMessage(action), cancellationToken);
    public Task SelectGameAsync(string gameId, CancellationToken cancellationToken = default) =>
        SendAsync(new GameSelectionMessage(gameId), cancellationToken);
    public Task GameActionAsync(string gameId, string action, System.Text.Json.JsonElement payload,
        CancellationToken cancellationToken = default) =>
        SendAsync(new GameActionMessage(gameId, action, payload), cancellationToken);
    public Task StartSoloMysteryAsync(CancellationToken cancellationToken = default) =>
        GameActionAsync("solo-mystery", "start",
            System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone(), cancellationToken);
    public Task SubmitMysteryActionAsync(string text, CancellationToken cancellationToken = default)
    {
        using var document = System.Text.Json.JsonDocument.Parse(
            System.Text.Json.JsonSerializer.Serialize(new { text }));
        return GameActionAsync("solo-mystery", "act", document.RootElement.Clone(), cancellationToken);
    }

    private Task SendAsync(IProtocolMessage message, CancellationToken cancellationToken) =>
        connection is null ? Task.FromException(new InvalidOperationException("Client is not connected.")) :
            connection.SendAsync(message, cancellationToken);
}
