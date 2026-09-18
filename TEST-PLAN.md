# Test plan

## Preflight

* `dotnet --info` shows the .NET 9 SDK.
* `dotnet build Coord.sln` completes with zero warnings and errors.
* `dotnet test Coord.sln` passes.
* `dotnet format Coord.sln --verify-no-changes` reports no changes.
* `dotnet run --project src/Coord -- --help`, `games`, and `version` return 0.

## Automated/offline evidence

The test suite covers admission and state transitions for the one-player Station
of Echoes mystery, deterministic provider repeatability, prompt construction,
JSON sanitization and answer-leak prevention, provider failures/timeouts,
generic protocol serialization, and existing multiplayer game regressions.
The default `ai.provider: none` makes these tests network-free and needs no key.

## Live Grok (opt-in)

In a private shell only, set `COORD_AI_PROVIDER=xai`, `XAI_API_KEY`, and
optionally `COORD_AI_MODEL`/`COORD_AI_TIMEOUT_SECONDS`, then start
`coord host --no-ui`. Confirm a solo session advances scenes, and inspect
client messages/logs to verify the solution is absent. Never put the key in a
file, command history, test output, or commit. Record model, timestamp, and
result as external evidence.

## Multiplayer regression

Run one host and two clients; verify existing two-player games still admit two
players and reject a third game participant, while selecting Station of Echoes
rejects a second connected player. Verify selection, turn, private-state, and
disconnect behavior for Battleship and Who Am I?.

## Failure/security checks

Use a mocked provider returning malformed JSON, markdown-wrapped JSON, an HTTP
error, a cancellation, and a response containing the hidden solution. Expected
results are bounded sanitized text, an explicit failed/aborted state, no
exception escaping the host loop, and no solution in any public protocol
message. Also verify API keys never appear in URLs, serialized protocol
messages, exceptions, or repository files.
