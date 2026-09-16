# Coord

Coord is a cooperative game hosting project. It includes a first playable vertical slice:
the shared-mystery **Who Am I?** game.

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

`--no-ui` is accepted for scripted/headless runs; without it, Terminal.Gui runs alongside the networking task. The current host uses the fixed development room code `COORD`; lobby state is in memory. Reconnecting with the same player name restores the connected status, but no durable identity/authentication exists. Admission is represented explicitly by an approval message followed by the admission result. Lobby and player-status messages are broadcast to connected clients.

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

The provider seam is `IIdentityProvider`. The default host uses the deterministic
`ManualIdentityProvider` and an in-memory identity, so this slice needs no
credentials or network calls. A web/AI provider can be added later without
changing game rules; it must return one identity before the game starts.

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
```

The repository is not configured with Git by this setup; no Git or SSH configuration is required.
