namespace Coord.Host;

public enum AdmissionState { Waiting, Accepted, Rejected, Disconnected }

public sealed record AdmissionResult(AdmissionState State, string PlayerId, string Reason);

public sealed class AdmissionStateMachine(string roomCode, int capacity = 16)
{
    private readonly object sync = new();
    private readonly Dictionary<string, PlayerRecord> players = new(StringComparer.Ordinal);
    public string RoomCode { get; } = roomCode;
    public int Capacity { get; } = capacity;
    public IReadOnlyCollection<PlayerRecord> Players
    {
        get { lock (sync) return players.Values.ToArray(); }
    }

    public AdmissionResult Admit(string playerId, string name, string requestedRoom)
    {
        lock (sync)
        {
            if (!string.Equals(RoomCode, requestedRoom, StringComparison.OrdinalIgnoreCase))
                return new(AdmissionState.Rejected, playerId, "Room code does not match.");
            if (players.ContainsKey(playerId))
            {
                var existing = players[playerId];
                players[playerId] = existing with { Connected = true };
                return new(AdmissionState.Accepted, playerId, "Already admitted.");
            }
            if (players.Count >= Capacity)
                return new(AdmissionState.Rejected, playerId, "Room is full.");
            players[playerId] = new(playerId, name, true, false);
            return new(AdmissionState.Waiting, playerId, "Waiting for host approval.");
        }
    }

    public AdmissionResult Approve(string playerId, bool approved, string reason)
    {
        lock (sync)
        {
            if (!players.ContainsKey(playerId))
                return new(AdmissionState.Rejected, playerId, "Admission request not found.");
            if (!approved)
            {
                players.Remove(playerId);
                return new(AdmissionState.Rejected, playerId, reason);
            }
            return new(AdmissionState.Accepted, playerId, reason);
        }
    }

    public AdmissionResult Disconnect(string playerId)
    {
        lock (sync)
        {
            if (!players.TryGetValue(playerId, out var player))
                return new(AdmissionState.Disconnected, playerId, "Unknown player.");
            players[playerId] = player with { Connected = false };
            return new(AdmissionState.Disconnected, playerId, "Disconnected.");
        }
    }

    public void SetReady(string playerId, bool ready)
    {
        lock (sync)
        {
            if (players.TryGetValue(playerId, out var player))
                players[playerId] = player with { Ready = ready };
        }
    }
}

public sealed record PlayerRecord(string Id, string Name, bool Connected, bool Ready);
