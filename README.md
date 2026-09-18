# coord

coord is a host-and-client terminal platform for running multiplayer activities.
It provides shared session management, player admission, turn coordination,
networking, persistence, and terminal interfaces so each activity can focus on
its own rules.

The platform is designed to support multiple activities rather than hard-code one
set of rules. Each activity can define its own setup, state machine, turn
handling, messages, persistence needs, and terminal views while reusing coord's
networking, admission, configuration, storage, and UI infrastructure. The host
can run without participating, so it can facilitate a session for other clients;
the host operator may also connect separately as a client when desired.

coord is an early-stage .NET 9 application with a reusable host/client foundation.
It currently includes six built-in activities and is structured so additional
activities can define their own rules, state, messages, persistence, and terminal
views without redesigning the networking layer.

## Requirements

Install the .NET 9 SDK or newer and confirm it is available:

```text
dotnet --info
```

On Windows, install the official SDK from [dotnet.microsoft.com/download](https://dotnet.microsoft.com/download/dotnet/9.0) and reopen PowerShell after installation. On Unix-like systems, install the SDK using the package manager or Microsoft's install instructions, then ensure `dotnet` is on `PATH`.

## Run

From the repository root, run the CLI directly:

```text
dotnet run --project src/Coord -- --help
dotnet run --project src/Coord -- host --help
dotnet run --project src/Coord -- client --help
dotnet run --project src/Coord -- version
```

Start a host (Ctrl+C stops it), then connect one or more clients in another terminal:

```text
dotnet run --project src/Coord -- host
dotnet run --project src/Coord -- client --name Ada --room COORD
```

Both commands accept `--config path` for a JSON configuration file. The file shape is:

```json
{"network":{"address":"127.0.0.1:4242","connectTimeout":"00:00:10"},"storage":{"dataDirectory":"data"},"ai":{"provider":"none"}}
```

`--no-ui` is accepted for scripted/headless runs; without it, Terminal.Gui runs alongside the networking task. The host UI opens on a neutral lobby/setup screen: it lists every activity (including `solo-mystery`), shows the room/settings/player overview, and has no game-specific controls selected. Choose an activity and configure/start it before its controls appear. Clients likewise show that they are waiting for the host, then show the selected activity while it is being configured. The current host uses the fixed development room code `COORD`; lobby state is in memory. Reconnecting with the same player name restores the connected status, but no durable identity/authentication exists. Admission is represented explicitly by an approval message followed by the admission result. Lobby and player-status messages are broadcast to connected clients.

## Games and playable flow

List the built-in games (or request one game's short help) with:

```text
dotnet run --project src/Coord -- games
dotnet run --project src/Coord -- games battleship
```

Every game has exactly two admitted players, alternates turns, rejects actions
from the wrong player, and broadcasts only public state. The available rules are:

* **Word Duel**: players submit a unique, letter-only word in the configured
  category and length range. Each accepted word earns the configured points;
  duplicate and invalid submissions earn zero and do not change the turn.
  Validation is offline and deterministic behind `IWordValidator`.
* **Battleship**: each player supplies a non-overlapping fleet layout. Shots
  alternate; each player sees their own layout and only their own hit/miss
  results, never the opponent's ships.
* **Tic-Tac-Toe**: players place marks on a 3x3 board. Three in a row wins;
  a full board is a draw.
* **Connect Four**: players drop pieces into a seven-column, six-row board.
  Four connected pieces wins; a full board is a draw.

The generic wire identifiers are `gameSelection`, `gameAction`,
`gameSetup`, `gameStart`, `gameState`, `gamePrivateState`, `gameTurn`, and
`gameResult`; game-specific payloads remain domain-owned.

**Station of Echoes** (`solo-mystery`) is an AI-hosted text mystery with exactly
one admitted player. It transitions through configuring, active, won, failed,
or aborted; use generic `gameAction` `start` and `act` messages. The hidden
solution is server-only and is never serialized to clients. The default
deterministic provider is offline; selecting `xai` uses the existing Grok
adapter with `XAI_API_KEY` supplied only through the environment.

## Who Am I? playable flow

1. Start the host and connect two or more clients.
2. Configure and start the game by sending `gameSetup` with a category followed by
   `gameStart` (the host UI shows the current game state).
3. On a client's turn, send a concise `gameQuestion` ending in `?`, or a
   `gameGuess`. Questions are answered with `gameAnswer` (`Yes`, `No`,
   `Uncertain`, or `Rephrase`). Uncertain and rephrase responses do not consume
   the turn; guesses always do (unless correct, which ends the game).
4. `gameControl` supports `skip`, `pause`, and `resume`. Public state and history
   are broadcast as `gameState`; the identity is never included.

The provider seam is `IIdentityProvider`, with `IAiProvider` and
`ISearchProvider` available to adapters. `ProviderFactory` selects the safe
`manual`/`none` providers by default; these are deterministic and never make
network calls or require credentials. OpenAI Responses, Gemini REST, and xAI
Grok REST adapters are available when explicitly selected. Their keys are read
only from environment variables and are never sent to clients or included in
errors.

### AI provider setup and security

The default is offline (`ai.provider: none`). To enable a provider, set
`COORD_AI_PROVIDER` (`openai`, `gemini`, or `xai`), optionally
`COORD_AI_MODEL`, `COORD_AI_BASE_URL`, and `COORD_AI_TIMEOUT_SECONDS`.
Set exactly one provider key: `OPENAI_API_KEY`, `GEMINI_API_KEY`, or
`XAI_API_KEY`. The xAI default base URL is `https://api.x.ai/v1`.
`Coord__Ai__Provider`, `Coord__Ai__Model`, `Coord__Ai__BaseUrl`,
`Coord__Ai__TimeoutSeconds`, and `Coord__Ai__<OpenAi|Gemini|Xai>ApiKey`
(or the unprefixed `Ai__...` equivalents) are .NET
configuration/user-secrets-compatible environment names.
For local development, store secrets in the user-secrets store and expose
them to the process using your host's normal .NET configuration bootstrap;
never put keys in JSON, source control, wire messages, or logs.

`copilot` is intentionally unsupported: this application does not have an
official Copilot SDK/authentication runtime and does not pretend that an API
key is sufficient. Provider terms, privacy, retention, and generated-content
policies remain the operator's responsibility.

Hosts persist research dossiers and player statistics below
`storage.dataDirectory` using JSON and atomic-ish replace writes. Identity
dossiers are recorded when a game starts, and questions, guesses, wins, and
leaderboard statistics are updated as the game proceeds. This persistence is
host-local and does not change the wire protocol or expose the selected
identity to clients.

TLS is an isolated opt-in transport: set `network.useTls` to `true` and provide
`network.serverCertificatePath` (and optionally `serverCertificatePassword`) on the host.
The client must use the same setting and a certificate trusted by the operating system.
Plain TCP remains the development default so the sample configuration works without provisioning certificates.

Build a binary:

```text
dotnet build Coord.sln
```

## UI dependencies

The full-screen, event-driven TUI will use [Terminal.Gui](https://github.com/tui-cs/Terminal.Gui), pinned to **1.17.1**, the newest stable package compatible with the current .NET 9 target. It is cross-platform, supports keyboard and mouse input, and provides layout, menus, forms, tables, and other built-in views. Terminal.Gui 2.x is available, but its current stable packages target .NET 10, so it is deferred until the project target is intentionally upgraded.

[Spectre.Console](https://spectreconsole.net/) remains a complementary dependency for startup output, diagnostics, tables, and prompts where a full-screen event loop is unnecessary. UI dependencies are confined to the application/UI layer; host, client, protocol, storage, games, AI, and search contracts remain UI-independent.

## Validate

```text
dotnet build Coord.sln
dotnet test Coord.sln
dotnet format Coord.sln --verify-no-changes
```

Git is configured for the private `k0pfnicker/coord` repository. SSH setup is
only required when pushing changes; building and running coord locally does not
depend on Git.
