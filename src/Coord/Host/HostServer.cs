using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Security.Authentication;
using System.Collections.Concurrent;
using Coord.Config;
using Coord.Protocol;
using Coord.Games;
using Coord.Ai;
using Coord.Research;
using Coord.Storage;
using System.Text.Json;

namespace Coord.Host;

public sealed class HostServer(CoordConfig config)
{
    private readonly AdmissionStateMachine admission = new("COORD", 16);
    private TcpListener? listener;
    private readonly ConcurrentDictionary<string, JsonLineConnection> connections = new();
    private bool statsRecorded;
    private bool identityRecorded;
    private int persistedHistoryCount;
    private readonly GameRegistry registry = CreateRegistry();
    private string? selectedGame;
    private readonly WordDuelGame wordDuel = new();
    private readonly BattleshipGame battleship = new();
    private readonly TicTacToeGame ticTacToe = new();
    private readonly ConnectFourGame connectFour = new();
    private readonly SoloMysteryGame soloMystery = new(CreateMysteryProvider(config.Ai));
    public CoordConfig Config { get; } = ValidateConfig(config);
    public IResearchRepository Research { get; } = new JsonResearchRepository(new FileStorage(config.Storage.DataDirectory));
    public WhoAmIGame Game { get; } = new(ProviderFactory.CreateIdentity(config.Ai));
    public SoloMysteryGame SoloMystery => soloMystery;
    public string RoomCode => admission.RoomCode;
    public string? SelectedGame => selectedGame;
    public IReadOnlyList<IGame> AvailableGames => registry.List();
    public Task<IReadOnlyList<PlayerStats>> LoadLeaderboardAsync(CancellationToken cancellationToken = default) =>
        Research.LoadLeaderboardAsync(cancellationToken);

    private static CoordConfig ValidateConfig(CoordConfig value)
    {
        ConfigValidator.Validate(value);
        return value;
    }

    private static GameRegistry CreateRegistry()
    {
        var registry = new GameRegistry();
        registry.Register(new BuiltInGame("word-duel", "Word Duel"));
        registry.Register(new BuiltInGame("battleship", "Battleship"));
        registry.Register(new BuiltInGame("tic-tac-toe", "Tic-Tac-Toe"));
        registry.Register(new BuiltInGame("connect-four", "Connect Four"));
        registry.Register(new BuiltInGame("who-am-i", "Who Am I?"));
        registry.Register(new BuiltInGame("solo-mystery", "Station of Echoes (AI mystery)"));
        return registry;
    }

    // Keep the default game entirely offline and deterministic; network providers are opt-in.
    private static ISoloMysteryProvider CreateMysteryProvider(AiConfig ai) =>
        ai.Provider.Equals("none", StringComparison.OrdinalIgnoreCase) ||
        ai.Provider.Equals("manual", StringComparison.OrdinalIgnoreCase)
            ? new DeterministicMysteryProvider()
            : new AiMysteryProvider(ProviderFactory.CreateAi(ai));


    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var (_, port) = ConfigLoader.ParseAddress(Config.Network.Address);
        listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        X509Certificate2? certificate = null;
        if (Config.Network.UseTls)
        {
            if (string.IsNullOrWhiteSpace(Config.Network.ServerCertificatePath))
                throw new InvalidOperationException("TLS requires network.serverCertificatePath.");
            certificate = X509CertificateLoader.LoadPkcs12FromFile(
                Config.Network.ServerCertificatePath, Config.Network.ServerCertificatePassword);
        }
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var tcp = await listener.AcceptTcpClientAsync(cancellationToken);
                _ = AcceptClientAsync(tcp, certificate, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally { listener.Stop(); }
    }

    private async Task AcceptClientAsync(TcpClient tcp, X509Certificate2? certificate, CancellationToken cancellationToken)
    {
        try
        {
            var connection = await TcpLineConnection.AcceptAsync(tcp, certificate, cancellationToken);
            await HandleClientAsync(connection, cancellationToken);
        }
        catch (IOException) { tcp.Dispose(); }
        catch (AuthenticationException) { tcp.Dispose(); }
    }

    private async Task HandleClientAsync(JsonLineConnection connection, CancellationToken cancellationToken)
    {
        string? playerId = null;
        try
        {
            await foreach (var message in connection.ReadAllAsync(cancellationToken))
            {
                switch (message)
                {
                    case HelloMessage hello:
                        await connection.SendAsync(new HelloMessage("coord-host", ProtocolConstants.CurrentVersion), cancellationToken);
                        break;
                    case AdmissionRequest request:
                        playerId = request.PlayerName.Trim();
                        if (selectedGame == "solo-mystery" && admission.Players.Any(p => p.Connected))
                        {
                            await connection.SendAsync(new AdmissionResponse(false, RoomCode, playerId,
                                "Solo Mystery admits exactly one player."), cancellationToken);
                            break;
                        }
                        var result = admission.Admit(playerId, request.PlayerName, request.RoomCode);
                        var approved = result.State is AdmissionState.Waiting or AdmissionState.Accepted;
                        await connection.SendAsync(new AdmissionApproval(playerId, approved,
                            approved ? "Host approved admission." : result.Reason), cancellationToken);
                        if (approved) result = admission.Approve(playerId, true, "Admitted.");
                        await connection.SendAsync(new AdmissionResponse(
                            result.State == AdmissionState.Accepted, RoomCode, playerId, result.Reason), cancellationToken);
                        if (result.State == AdmissionState.Accepted)
                        {
                            connections[playerId] = connection;
                            Game.AddPlayer(playerId);
                            wordDuel.AddPlayer(playerId);
                            battleship.AddPlayer(playerId);
                            ticTacToe.AddPlayer(playerId);
                            connectFour.AddPlayer(playerId);
                            if (selectedGame == "solo-mystery") soloMystery.AddPlayer(playerId);
                            await BroadcastAsync(CurrentLobby(), cancellationToken);
                            await connection.SendAsync(new GameSelectionMessage(selectedGame), cancellationToken);
                            await BroadcastAsync(new PlayerStatus(playerId, request.PlayerName, true, false), cancellationToken);
                            if (selectedGame == "who-am-i") await BroadcastGameAsync(cancellationToken);
                        }
                        break;
                    case GameSetupMessage setup when playerId is not null && selectedGame == "who-am-i":
                        await SendGameActionAsync(Game.Configure(setup.Category), cancellationToken);
                        break;
                    case GameStartMessage start when playerId is not null && selectedGame == "who-am-i":
                        if (!string.IsNullOrWhiteSpace(start.Category)) Game.Configure(start.Category);
                        await SendGameActionAsync(await Game.StartAsync(cancellationToken), cancellationToken);
                        break;
                    case GameQuestionMessage question when playerId is not null && selectedGame == "who-am-i":
                        await SendGameActionAsync(Game.SubmitQuestion(playerId, question.Question), cancellationToken);
                        break;
                    case GameAnswerMessage answer when playerId is not null && selectedGame == "who-am-i" &&
                        Enum.TryParse<WhoAmIAnswer>(answer.Answer, true, out var parsedAnswer):
                        await SendGameActionAsync(Game.AnswerQuestion(playerId, parsedAnswer), cancellationToken);
                        break;
                    case GameGuessMessage guess when playerId is not null && selectedGame == "who-am-i":
                        await SendGameActionAsync(Game.Guess(playerId, guess.Guess), cancellationToken);
                        break;
                    case GameControlMessage control when playerId is not null && selectedGame == "who-am-i":
                        await SendGameActionAsync(control.Action.ToLowerInvariant() switch
                        {
                            "skip" => Game.Skip(playerId),
                            "pause" => Game.Pause(),
                            "resume" => Game.Resume(),
                            _ => new WhoAmIAction(false, "Unknown game action.", Game.State)
                        }, cancellationToken);
                        break;
                    case GameSelectionMessage selection when playerId is not null && selection.GameId is not null:
                        await HandleGameSelectionAsync(selection.GameId, cancellationToken);
                        break;
                    case GameActionMessage action when playerId is not null:
                        await HandleGameActionAsync(playerId, action, cancellationToken);
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (IOException) { }
        finally
        {
            if (playerId is not null) { admission.Disconnect(playerId); Game.RemovePlayer(playerId); }
            if (playerId is not null && selectedGame == "solo-mystery" &&
                soloMystery.State.Phase == SoloMysteryPhase.Active)
                soloMystery.Abort("The player disconnected.");
            if (playerId is not null)
            {
                connections.TryRemove(playerId, out _);
                _ = BroadcastAsync(new PlayerStatus(playerId, playerId, false, false), CancellationToken.None);
                _ = BroadcastAsync(CurrentLobby(), CancellationToken.None);
                _ = BroadcastGameAsync(CancellationToken.None);
            }
            await connection.DisposeAsync();
        }
    }

    private LobbyState CurrentLobby() => new(RoomCode, admission.Players
        .Select(p => new PlayerInfo(p.Id, p.Name, p.Connected, p.Ready)).ToArray());

    private async Task BroadcastAsync(IProtocolMessage message, CancellationToken cancellationToken)
    {
        foreach (var peer in connections.Values.Distinct())
        {
            try { await peer.SendAsync(message, cancellationToken); }
            catch (IOException) { }
        }
    }

    private async Task SendGameActionAsync(WhoAmIAction action, CancellationToken cancellationToken)
    {
        await PersistGameDataAsync(action, cancellationToken);
        await BroadcastGameAsync(cancellationToken);
        if (!action.Accepted && connections.TryGetValue(action.State.CurrentPlayerId ?? "", out var peer))
            await peer.SendAsync(new GameResultMessage(action.Reason, action.State.WinnerId), cancellationToken);
    }

    private async Task PersistGameDataAsync(WhoAmIAction action, CancellationToken cancellationToken)
    {
        var state = action.State;
        if (action.Accepted && state.History.Count > persistedHistoryCount)
        {
            var item = state.History[^1];
            persistedHistoryCount = state.History.Count;
            if (item.Kind == "question")
                await Research.UpdateStatsAsync(item.PlayerId, stats => stats.RecordQuestion(), cancellationToken);
            else if (item.Kind == "guess")
                await Research.UpdateStatsAsync(item.PlayerId,
                    stats => stats.RecordGuess(string.Equals(item.Response, "Correct", StringComparison.Ordinal)),
                    cancellationToken);
        }
        if (action.Accepted && state.Phase == WhoAmIPhase.Active &&
            Game.SelectedIdentity is { } identity && state.History.Count == 0)
        {
            if (identityRecorded) return;
            identityRecorded = true;
            await Research.SaveIdentityAsync(new IdentityDossier(
                state.Category ?? "unknown", identity.Name, identity.Aliases, DateTimeOffset.UtcNow,
                Config.Ai.Provider), cancellationToken);
        }
        if (action.Accepted && (state.Phase is WhoAmIPhase.Won or WhoAmIPhase.Aborted) && !statsRecorded)
        {
            statsRecorded = true;
            foreach (var player in state.Players)
                await Research.UpdateStatsAsync(player, stats => stats.RecordGame(player == state.WinnerId), cancellationToken);
        }
    }

    private Task BroadcastGameAsync(CancellationToken cancellationToken)
    {
        var state = Game.State;
        var message = new GameStateMessage(state.Phase.ToString(), state.Category, state.CurrentPlayerId,
            state.History.Select(h => new GameHistoryItem(h.PlayerId, h.Text, h.Kind, h.Response)).ToArray(),
            state.WinnerId, state.Result);
        return BroadcastAsync(message, cancellationToken);
    }

    private async Task HandleGameSelectionAsync(string gameId, CancellationToken cancellationToken)
    {
        var known = gameId.ToLowerInvariant() switch
        {
            "word-duel" => true,
            "battleship" => true,
            "tic-tac-toe" => true,
            "connect-four" => true,
            "solo-mystery" => true,
            "who-am-i" => true,
            _ => false
        };
        if (known && gameId.Equals("solo-mystery", StringComparison.OrdinalIgnoreCase))
        {
            var admitted = admission.Players.Where(p => p.Connected).ToArray();
            if (admitted.Length == 1 && soloMystery.AddPlayer(admitted[0].Id))
                selectedGame = "solo-mystery";
        }
        else if (known) selectedGame = gameId.ToLowerInvariant();
        await BroadcastAsync(new GameSelectionMessage(selectedGame), cancellationToken);
        if (selectedGame is not null) await BroadcastSelectedStateAsync(cancellationToken);
    }

    public Task SelectGameAsync(string gameId, CancellationToken cancellationToken = default) =>
        HandleGameSelectionAsync(gameId, cancellationToken);

    public async Task<string> StartSelectedGameAsync(string category = "",
        CancellationToken cancellationToken = default)
    {
        if (selectedGame is null) return "Choose a game first.";
        if (selectedGame == "who-am-i")
        {
            if (!string.IsNullOrWhiteSpace(category)) Game.Configure(category);
            var action = await Game.StartAsync(cancellationToken);
            await SendGameActionAsync(action, cancellationToken);
            return action.Reason;
        }

        GameMoveResult result;
        if (selectedGame == "solo-mystery")
        {
            var mystery = await soloMystery.StartAsync(cancellationToken);
            result = new(mystery.Accepted, mystery.Reason);
        }
        else
        {
            result = selectedGame switch
            {
                "word-duel" => wordDuel.Start(),
                "battleship" => battleship.Start(),
                "tic-tac-toe" => ticTacToe.Start(),
                "connect-four" => connectFour.Start(),
                _ => new(false, "Unknown game.")
            };
        }
        await BroadcastSelectedStateAsync(cancellationToken);
        return result.Reason;
    }

    private async Task HandleGameActionAsync(
        string playerId, GameActionMessage action, CancellationToken cancellationToken)
    {
        if (selectedGame is null ||
            !string.Equals(action.GameId, selectedGame, StringComparison.OrdinalIgnoreCase))
            return;

        GameMoveResult result = new(false, "Unsupported game action.");
        try
        {
            switch (selectedGame)
            {
                case "tic-tac-toe" when action.Action.Equals("start", StringComparison.OrdinalIgnoreCase):
                    result = ticTacToe.Start();
                    break;
                case "tic-tac-toe" when action.Action.Equals("move", StringComparison.OrdinalIgnoreCase):
                    result = ticTacToe.Move(playerId, Coordinate(action.Payload));
                    break;
                case "connect-four" when action.Action.Equals("start", StringComparison.OrdinalIgnoreCase):
                    result = connectFour.Start();
                    break;
                case "connect-four" when action.Action.Equals("drop", StringComparison.OrdinalIgnoreCase):
                    result = connectFour.Drop(playerId, action.Payload.GetProperty("column").GetInt32());
                    break;
                case "word-duel" when action.Action.Equals("start", StringComparison.OrdinalIgnoreCase):
                    result = wordDuel.Start();
                    break;
                case "word-duel" when action.Action.Equals("submit", StringComparison.OrdinalIgnoreCase):
                    var submission = wordDuel.Submit(playerId, action.Payload.GetProperty("word").GetString() ?? "");
                    result = new(submission.Accepted, submission.Reason);
                    break;
                case "battleship" when action.Action.Equals("start", StringComparison.OrdinalIgnoreCase):
                    result = battleship.Start();
                    break;
                case "battleship" when action.Action.Equals("layout", StringComparison.OrdinalIgnoreCase):
                    var ships = JsonSerializer.Deserialize<IReadOnlyList<ShipLayout>>(
                        action.Payload.GetProperty("ships").GetRawText());
                    result = ships is null
                        ? new(false, "A ship layout is required.")
                        : battleship.SetLayout(playerId, ships);
                    break;
                case "battleship" when action.Action.Equals("fire", StringComparison.OrdinalIgnoreCase):
                    var shot = battleship.Fire(playerId, Coordinate(action.Payload));
                    result = new(shot.Accepted, shot.Reason);
                    break;
                case "solo-mystery" when action.Action.Equals("start", StringComparison.OrdinalIgnoreCase):
                    var started = await soloMystery.StartAsync(cancellationToken);
                    result = new(started.Accepted, started.Reason);
                    break;
                case "solo-mystery" when action.Action.Equals("act", StringComparison.OrdinalIgnoreCase):
                    var text = action.Payload.GetProperty("text").GetString() ?? "";
                    var advanced = await soloMystery.SubmitAsync(playerId, text, cancellationToken);
                    result = new(advanced.Accepted, advanced.Reason);
                    break;
            }
        }
        catch (JsonException) { }
        catch (KeyNotFoundException) { }

        if (connections.TryGetValue(playerId, out var peer))
            await peer.SendAsync(new GameResultMessage(result.Reason, Winner(selectedGame)), cancellationToken);
        await BroadcastSelectedStateAsync(cancellationToken);
    }

    private async Task BroadcastSelectedStateAsync(CancellationToken cancellationToken)
    {
        if (selectedGame is null) return;
        var state = selectedGame switch
        {
            "word-duel" => JsonSerializer.SerializeToElement(wordDuel.State),
            "battleship" => JsonSerializer.SerializeToElement(battleship.State),
            "tic-tac-toe" => JsonSerializer.SerializeToElement(ticTacToe.State),
            "connect-four" => JsonSerializer.SerializeToElement(connectFour.State),
            "solo-mystery" => JsonSerializer.SerializeToElement(soloMystery.State),
            _ => JsonSerializer.SerializeToElement(new { })
        };
        await BroadcastAsync(new GameStateMessage(
            "active", null, CurrentPlayer(selectedGame), [], null, null, selectedGame),
            cancellationToken);
        await BroadcastAsync(new GameTurnMessage(CurrentPlayer(selectedGame)), cancellationToken);
        foreach (var entry in connections)
        {
            if (selectedGame == "battleship")
            {
                var privateState = JsonSerializer.SerializeToElement(battleship.ViewFor(entry.Key));
                try { await entry.Value.SendAsync(new GamePrivateStateMessage(selectedGame, privateState), cancellationToken); }
                catch (IOException) { }
            }
            try { await entry.Value.SendAsync(new GameActionMessage(selectedGame, "state", state), cancellationToken); }
            catch (IOException) { }
        }
    }

    private string? CurrentPlayer(string gameId) => gameId switch
    {
        "word-duel" => wordDuel.CurrentPlayerId,
        "battleship" => battleship.CurrentPlayerId,
        "tic-tac-toe" => ticTacToe.CurrentPlayerId,
        "connect-four" => connectFour.CurrentPlayerId,
        "solo-mystery" => soloMystery.State.PlayerId,
        _ => null
    };

    private string? Winner(string gameId) => gameId switch
    {
        "word-duel" => wordDuel.State.WinnerId,
        "battleship" => battleship.State.WinnerId,
        "tic-tac-toe" => ticTacToe.State.WinnerId,
        "connect-four" => connectFour.State.WinnerId,
        "solo-mystery" => soloMystery.State.Phase == SoloMysteryPhase.Won ? soloMystery.State.PlayerId : null,
        _ => null
    };

    private static BoardCoordinate Coordinate(JsonElement payload) =>
        new(payload.GetProperty("row").GetInt32(), payload.GetProperty("column").GetInt32());

    public async ValueTask DisposeAsync()
    {
        listener?.Stop();
        await Task.CompletedTask;
    }
}
