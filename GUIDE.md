# coord user guide

This guide explains how to run coord as a host, how to join as a player, and how
to configure a session.

## Requirements

coord currently targets .NET 9. Install the .NET 9 SDK or a newer compatible SDK:

```text
dotnet --info
```

Run commands from the repository root, or replace the project path with the path
to a published `coord` executable.

## Start a host

Start a host with the default configuration:

```text
dotnet run --project src/Coord -- host
```

The host prints the listening address and room code. Share those values with the
players who should join. Press `Ctrl+C` to stop the host.

The host application coordinates the room and can run without joining the activity
as a player. A host operator who wants to participate can open a second terminal
and run a client with their own player name.

For a non-interactive or scripted host:

```text
dotnet run --project src/Coord -- host --no-ui
```

The full-screen host interface starts in a neutral lobby/setup view. It lists all
available activities (including `solo-mystery`), the room/settings overview, and
the current player count. No activity is selected by default. Choose an activity
to reveal its setup controls, then configure/start it; game-specific controls are
not shown before that point. Clients display that they are waiting for the host,
then show the selected activity while it is being configured or started.

## Join as a player

Open another terminal and connect with the address configured by the host:

```text
dotnet run --project src/Coord -- client --name Ada --room COORD
```

Use the room code printed by the host instead of `COORD`. The default local
configuration listens on `127.0.0.1:4242`; for another computer on the same
network, the client configuration must use the host computer's LAN address.

For a non-interactive client:

```text
dotnet run --project src/Coord -- client --name Ada --room COORD --no-ui
```

The host approves or rejects admission. Use a distinct player name for each
participant. If a client disconnects, reconnect with the same name when the
activity and host still allow reconnection.

## Available activities

List activities:

```text
dotnet run --project src/Coord -- games
```

Show a short description:

```text
dotnet run --project src/Coord -- games connect-four
```

### Word Duel

Two players alternate submitting words for the configured category and length
constraints. Accepted words score points; duplicate or invalid submissions do not
score and do not advance the turn. The current validator is deterministic and
offline, so no API key is required.

### Battleship

Two players privately place their fleets and alternate firing at coordinates.
Each player sees their own fleet and shot results, while opponent ship positions
remain private. The public state contains only information that is safe to share.

### Tic-Tac-Toe

Two players alternate placing their mark on a 3x3 board. Three marks in a row
wins; a full board without a winner is a draw.

### Connect Four

Two players alternate dropping pieces into a seven-column, six-row board. A
player wins by connecting four pieces horizontally, vertically, or diagonally.
Filling the board without a winning line produces a draw.

### Who Am I?

Players take turns asking concise yes/no questions about one shared hidden
identity. The host/provider answers with `Yes`, `No`, `Uncertain`, or `Rephrase`.
An incorrect guess consumes the turn; a correct guess ends the activity.

### Station of Echoes (AI mystery)

Select `solo-mystery` for a single-player, host-driven text mystery. Exactly
one connected player is admitted. Start with generic `gameAction` `start`, then
submit `{"text":"..."}` using action `act`. The host returns sanitized scenes
and clues; the public state never contains the hidden solution and ends in
`won`, `failed`, or `aborted`. Offline/manual mode is deterministic.

## Configuration

Both `host` and `client` accept a JSON file with `--config`:

```text
dotnet run --project src/Coord -- host --config coord.json
dotnet run --project src/Coord -- client --config coord.json --name Ada --room COORD
```

Minimal example:

```json
{
  "network": {
    "address": "127.0.0.1:4242",
    "connectTimeout": "00:00:10",
    "useTls": false
  },
  "storage": {
    "dataDirectory": "data"
  },
  "ai": {
    "provider": "none"
  }
}
```

### Network settings

* `network.address` is the bind address and port for the host, and the target
  address for clients. It must use `host:port` format.
* `network.connectTimeout` controls how long a client waits to connect.
* `network.useTls` enables TLS. It must be enabled consistently by host and
  clients.
* `network.serverCertificatePath` points to the host's PKCS#12/PFX certificate.
* `network.serverCertificatePassword` is the optional certificate password.

Plain TCP is the development default. For a shared LAN, TLS is recommended:

```json
{
  "network": {
    "address": "0.0.0.0:4242",
    "connectTimeout": "00:00:10",
    "useTls": true,
    "serverCertificatePath": "certs/coord-host.pfx",
    "serverCertificatePassword": "change-this"
  }
}
```

Clients need the same address and `useTls` setting and must trust the host
certificate through the operating system. Do not commit certificates or
passwords to the repository.

### Storage settings

The host stores local JSON data below `storage.dataDirectory`, including research
dossiers, player statistics, and leaderboard information. Use a directory outside
the repository for personal or long-running sessions if preferred.

### Provider settings

`ai.provider` defaults to `none`. The `none`/`manual` providers are offline-safe
and deterministic. OpenAI Responses, Gemini REST, and xAI Grok REST can be
enabled explicitly. For live Grok, use environment variables only:

```powershell
$env:COORD_AI_PROVIDER = "openai"
$env:OPENAI_API_KEY = "<secret kept outside the repository>"
$env:COORD_AI_MODEL = "gpt-4o-mini" # optional
dotnet run --project src/Coord -- host --no-ui
```

For Grok, set `$env:COORD_AI_PROVIDER = "xai"` and
`$env:XAI_API_KEY = "<secret kept outside the repository>"`; optionally set
`$env:COORD_AI_MODEL` (default `grok-3-mini`). Never put keys in JSON, source
control, wire messages, or logs.

The exact supported variables are `COORD_AI_PROVIDER`, `COORD_AI_MODEL`,
`COORD_AI_BASE_URL`, `COORD_AI_TIMEOUT_SECONDS`, `OPENAI_API_KEY`,
`GEMINI_API_KEY`, and `XAI_API_KEY`. The .NET user-secrets-compatible names
`Coord__Ai__Provider`, `Coord__Ai__Model`, `Coord__Ai__BaseUrl`,
`Coord__Ai__TimeoutSeconds`, and `Coord__Ai__<OpenAi|Gemini|Xai>ApiKey` (or
the unprefixed `Ai__...` equivalents) are also accepted. API keys are server-only: they are not configuration-file
fields, client-visible data, or log/error content. Requests have cancellation
and a configurable timeout. Provider failures are explicit; they do not
silently fall back to a different provider.

`copilot` is rejected because coord has no official Copilot SDK/auth runtime;
an API key alone is not represented as support. Review each provider's terms,
privacy, retention, and generated-content policies before enabling it.

## Troubleshooting

* **The client cannot connect:** verify the host is running, the address and port
  match, and the operating-system firewall allows the port.
* **Admission is rejected:** check the room code, player name, and whether the
  host has already reached the activity's player limit.
* **The UI does not start:** use `--no-ui` to verify the network/session layer,
  then run the command from a real interactive terminal.
* **TLS fails:** verify that both sides use TLS, the PFX path/password is valid,
  and the client trusts the certificate.

## Build and validate

```text
dotnet build Coord.sln
dotnet test Coord.sln
dotnet format Coord.sln --verify-no-changes
```
