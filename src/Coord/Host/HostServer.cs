using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Security.Authentication;
using System.Collections.Concurrent;
using Coord.Config;
using Coord.Protocol;
using Coord.Games;

namespace Coord.Host;

public sealed class HostServer(CoordConfig config)
{
    private readonly AdmissionStateMachine admission = new("COORD", 16);
    private TcpListener? listener;
    private readonly ConcurrentDictionary<string, JsonLineConnection> connections = new();
    public WhoAmIGame Game { get; } = new(new ManualIdentityProvider(
        new Identity("Ada Lovelace", ["Ada"])));
    public CoordConfig Config { get; } = config;
    public string RoomCode => admission.RoomCode;

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
                            await BroadcastAsync(CurrentLobby(), cancellationToken);
                            await BroadcastAsync(new PlayerStatus(playerId, request.PlayerName, true, false), cancellationToken);
                            await BroadcastGameAsync(cancellationToken);
                        }
                        break;
                    case GameSetupMessage setup when playerId is not null:
                        await SendGameActionAsync(Game.Configure(setup.Category), cancellationToken);
                        break;
                    case GameStartMessage start when playerId is not null:
                        if (!string.IsNullOrWhiteSpace(start.Category)) Game.Configure(start.Category);
                        await SendGameActionAsync(await Game.StartAsync(cancellationToken), cancellationToken);
                        break;
                    case GameQuestionMessage question when playerId is not null:
                        await SendGameActionAsync(Game.SubmitQuestion(playerId, question.Question), cancellationToken);
                        break;
                    case GameAnswerMessage answer when playerId is not null &&
                        Enum.TryParse<WhoAmIAnswer>(answer.Answer, true, out var parsedAnswer):
                        await SendGameActionAsync(Game.AnswerQuestion(playerId, parsedAnswer), cancellationToken);
                        break;
                    case GameGuessMessage guess when playerId is not null:
                        await SendGameActionAsync(Game.Guess(playerId, guess.Guess), cancellationToken);
                        break;
                    case GameControlMessage control when playerId is not null:
                        await SendGameActionAsync(control.Action.ToLowerInvariant() switch
                        {
                            "skip" => Game.Skip(playerId),
                            "pause" => Game.Pause(),
                            "resume" => Game.Resume(),
                            _ => new WhoAmIAction(false, "Unknown game action.", Game.State)
                        }, cancellationToken);
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (IOException) { }
        finally
        {
            if (playerId is not null) { admission.Disconnect(playerId); Game.RemovePlayer(playerId); }
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
        await BroadcastGameAsync(cancellationToken);
        if (!action.Accepted && connections.TryGetValue(action.State.CurrentPlayerId ?? "", out var peer))
            await peer.SendAsync(new GameResultMessage(action.Reason, action.State.WinnerId), cancellationToken);
    }

    private Task BroadcastGameAsync(CancellationToken cancellationToken)
    {
        var state = Game.State;
        var message = new GameStateMessage(state.Phase.ToString(), state.Category, state.CurrentPlayerId,
            state.History.Select(h => new GameHistoryItem(h.PlayerId, h.Text, h.Kind, h.Response)).ToArray(),
            state.WinnerId, state.Result);
        return BroadcastAsync(message, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        listener?.Stop();
        await Task.CompletedTask;
    }
}
