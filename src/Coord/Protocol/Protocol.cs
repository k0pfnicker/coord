using System.Text.Json;
using System.Text.Json.Serialization;

namespace Coord.Protocol;

public static class ProtocolConstants
{
    public const int MajorVersion = 1;
    public const int MinorVersion = 0;
    public static ProtocolVersion CurrentVersion => new(MajorVersion, MinorVersion);
}

public sealed record ProtocolVersion(int Major, int Minor);

public interface IProtocolMessage;

public sealed record HelloMessage(string Name, ProtocolVersion Protocol) : IProtocolMessage;
public sealed record AdmissionRequest(string RoomCode, string PlayerName) : IProtocolMessage;
public sealed record AdmissionApproval(string PlayerId, bool Approved, string Reason) : IProtocolMessage;
public sealed record AdmissionResponse(bool Accepted, string RoomCode, string PlayerId, string Reason) : IProtocolMessage;
public sealed record LobbyState(string RoomCode, IReadOnlyList<PlayerInfo> Players) : IProtocolMessage;
public sealed record PlayerStatus(string PlayerId, string Name, bool Connected, bool Ready) : IProtocolMessage;
public sealed record DisconnectMessage(string Reason) : IProtocolMessage;
public sealed record PlayerInfo(string Id, string Name, bool Connected, bool Ready);
public sealed record GameStartMessage(string Category) : IProtocolMessage;
public sealed record GameSetupMessage(string Category) : IProtocolMessage;
public sealed record GameQuestionMessage(string PlayerId, string Question) : IProtocolMessage;
public sealed record GameAnswerMessage(string PlayerId, string Answer) : IProtocolMessage;
public sealed record GameGuessMessage(string PlayerId, string Guess) : IProtocolMessage;
public sealed record GameControlMessage(string Action) : IProtocolMessage;
public sealed record GameTurnMessage(string? PlayerId) : IProtocolMessage;
public sealed record GameStateMessage(string Phase, string? Category, string? CurrentPlayerId,
    IReadOnlyList<GameHistoryItem> History, string? WinnerId, string? Result, string GameId = "who-am-i") : IProtocolMessage;
public sealed record GameHistoryItem(string PlayerId, string Text, string Kind, string? Response);
public sealed record GameResultMessage(string Result, string? WinnerId) : IProtocolMessage;
// Generic game routing messages keep the wire contract independent from a game's domain model.
public sealed record GameSelectionMessage(string? GameId) : IProtocolMessage;
public sealed record GameActionMessage(string GameId, string Action, JsonElement Payload) : IProtocolMessage;
public sealed record GamePrivateStateMessage(string GameId, JsonElement State) : IProtocolMessage;

public sealed record ProtocolEnvelope(int VersionMajor, int VersionMinor, string Type, JsonElement Payload);

public static class ProtocolCodec
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string Serialize(IProtocolMessage message)
    {
        var (type, payload) = message switch
        {
            HelloMessage value => ("hello", JsonSerializer.SerializeToElement(value, Options)),
            AdmissionRequest value => ("admission", JsonSerializer.SerializeToElement(value, Options)),
            AdmissionApproval value => ("approval", JsonSerializer.SerializeToElement(value, Options)),
            AdmissionResponse value => ("admissionResult", JsonSerializer.SerializeToElement(value, Options)),
            LobbyState value => ("lobby", JsonSerializer.SerializeToElement(value, Options)),
            PlayerStatus value => ("playerStatus", JsonSerializer.SerializeToElement(value, Options)),
            DisconnectMessage value => ("disconnect", JsonSerializer.SerializeToElement(value, Options)),
            GameStartMessage value => ("gameStart", JsonSerializer.SerializeToElement(value, Options)),
            GameSetupMessage value => ("gameSetup", JsonSerializer.SerializeToElement(value, Options)),
            GameQuestionMessage value => ("gameQuestion", JsonSerializer.SerializeToElement(value, Options)),
            GameAnswerMessage value => ("gameAnswer", JsonSerializer.SerializeToElement(value, Options)),
            GameGuessMessage value => ("gameGuess", JsonSerializer.SerializeToElement(value, Options)),
            GameControlMessage value => ("gameControl", JsonSerializer.SerializeToElement(value, Options)),
            GameTurnMessage value => ("gameTurn", JsonSerializer.SerializeToElement(value, Options)),
            GameStateMessage value => ("gameState", JsonSerializer.SerializeToElement(value, Options)),
            GameResultMessage value => ("gameResult", JsonSerializer.SerializeToElement(value, Options)),
            GameSelectionMessage value => ("gameSelection", JsonSerializer.SerializeToElement(value, Options)),
            GameActionMessage value => ("gameAction", JsonSerializer.SerializeToElement(value, Options)),
            GamePrivateStateMessage value => ("gamePrivateState", JsonSerializer.SerializeToElement(value, Options)),
            _ => throw new ArgumentOutOfRangeException(nameof(message))
        };
        return JsonSerializer.Serialize(new ProtocolEnvelope(
            ProtocolConstants.MajorVersion, ProtocolConstants.MinorVersion, type, payload), Options);
    }

    public static IProtocolMessage Deserialize(string line)
    {
        var envelope = JsonSerializer.Deserialize<ProtocolEnvelope>(line, Options)
            ?? throw new JsonException("Protocol message is empty.");
        if (envelope.VersionMajor != ProtocolConstants.MajorVersion)
            throw new JsonException($"Unsupported protocol major version {envelope.VersionMajor}.");

        return envelope.Type switch
        {
            "hello" => envelope.Payload.Deserialize<HelloMessage>(Options)!,
            "admission" => envelope.Payload.Deserialize<AdmissionRequest>(Options)!,
            "approval" => envelope.Payload.Deserialize<AdmissionApproval>(Options)!,
            "admissionResult" => envelope.Payload.Deserialize<AdmissionResponse>(Options)!,
            "lobby" => envelope.Payload.Deserialize<LobbyState>(Options)!,
            "playerStatus" => envelope.Payload.Deserialize<PlayerStatus>(Options)!,
            "disconnect" => envelope.Payload.Deserialize<DisconnectMessage>(Options)!,
            "gameStart" => envelope.Payload.Deserialize<GameStartMessage>(Options)!,
            "gameSetup" => envelope.Payload.Deserialize<GameSetupMessage>(Options)!,
            "gameQuestion" => envelope.Payload.Deserialize<GameQuestionMessage>(Options)!,
            "gameAnswer" => envelope.Payload.Deserialize<GameAnswerMessage>(Options)!,
            "gameGuess" => envelope.Payload.Deserialize<GameGuessMessage>(Options)!,
            "gameControl" => envelope.Payload.Deserialize<GameControlMessage>(Options)!,
            "gameTurn" => envelope.Payload.Deserialize<GameTurnMessage>(Options)!,
            "gameState" => envelope.Payload.Deserialize<GameStateMessage>(Options)!,
            "gameResult" => envelope.Payload.Deserialize<GameResultMessage>(Options)!,
            "gameSelection" => envelope.Payload.Deserialize<GameSelectionMessage>(Options)!,
            "gameAction" => envelope.Payload.Deserialize<GameActionMessage>(Options)!,
            "gamePrivateState" => envelope.Payload.Deserialize<GamePrivateStateMessage>(Options)!,
            _ => throw new JsonException($"Unknown message type '{envelope.Type}'.")
        };
    }
}
