# Game port analysis

This document compares coord's current implementations with related terminal-game
repositories and records what should be reused, ported, or reimplemented.

The comparison is based on coord's source and the upstream repositories at the
linked revisions. “Port” means reusing a small algorithm or interaction idea;
it does not mean embedding an unrelated terminal loop into coord.

## Executive summary

coord already has the stronger foundation for networked multiplayer:

- Server-authoritative game state
- Versioned host/client protocol
- Turn and player validation
- Public/private state separation
- Reconnect-aware admission
- Terminal.Gui integration
- Focused automated tests

The upstream projects are generally better references for standalone presentation
and some AI algorithms. Their input loops, rendering systems, and mutable global
state should not be copied directly into coord.

**Recommendation:** keep coord's current rule engines, then selectively
reimplement upstream improvements in C# behind coord's existing game and protocol
boundaries.

## Sources and licensing

All listed repositories below use an MIT license at the revisions reviewed.
If code or data is copied, preserve the applicable copyright and license notice.
Word lists and other bundled data can have separate provenance and license
requirements; do not copy those automatically.

| Repository | Relevant material | Recommendation |
|---|---|---|
| [ayu5h-raj/terminal-games](https://github.com/ayu5h-raj/terminal-games) | Wordle-style engine, keyboard input, colored feedback, daily state | Reimplement the interaction ideas; do not copy word lists without checking provenance |
| [Seniru/cli-games](https://github.com/Seniru/cli-games) | Tic-Tac-Toe rules and minimax reference | Use as an AI reference only |
| [paarthsiloiya/console-games-collection](https://github.com/paarthsiloiya/console-games-collection) | Aggregator for Battleship and Connect Four submodules | Inspect the actual submodules; preserve their individual notices if copying |
| [arasgungore/console-games](https://github.com/arasgungore/console-games) | C terminal-game menu and real-time loop patterns | Reference only; avoid platform-specific console assumptions |
| [avdaredevil/PowerSneks](https://github.com/avdaredevil/PowerSneks) | Tick/state/persistence concepts for real-time games | Reference only; its socket layer is not a ready network implementation |

## coord baseline

The current deterministic engines live in
[`src/Coord/Games/DeterministicGames.cs`](../src/Coord/Games/DeterministicGames.cs).
They already cover:

- Word Duel validation, duplicate handling, scoring, and turn order
- Battleship layout validation, shots, hits, wins, and player-specific views
- Tic-Tac-Toe legal moves, wins, draws, and stable player symbols
- Connect Four gravity, wins, draws, and stable player symbols

Generic game routing is provided through
[`src/Coord/Protocol/Protocol.cs`](../src/Coord/Protocol/Protocol.cs), while
host routing and broadcasting are implemented in
[`src/Coord/Host/HostServer.cs`](../src/Coord/Host/HostServer.cs).

## Word Duel and Wordle-style games

The upstream terminal-games project provides a much more polished Wordle-style
experience:

- Hidden target word
- Limited guesses
- Green/yellow/grey feedback
- Duplicate-aware evaluation
- On-screen keyboard
- Daily/random modes
- Separation between engine state and rendering

That is a different game from coord's current Word Duel, which is an alternating
two-player category/submission activity. The best approach is therefore:

1. Keep Word Duel as its own activity.
2. If desired, add a separate Wordle-style activity.
3. Reimplement the target/guess/feedback model in C#.
4. Keep the answer and valid-word set server-side.
5. Use Terminal.Gui controls and private/public protocol state as appropriate.

Do not import upstream word lists until their individual data licenses and
provenance are recorded.

## Battleship

The upstream console-battleship implementation is useful for:

- Standard fleet names and sizes
- Orientation controls
- Placement confirmation and reset
- Hit, miss, and destroyed-ship indicators
- Readable fleet presentation

coord's architecture is better suited to the multiplayer requirements because
the host owns layouts and shots and can provide caller-specific private state.

The recommended work is to reimplement the upstream interaction ideas while
retaining coord's engine:

- Add standard fleet metadata and ship sizes
- Add explicit sunk-ship information
- Add placement preview, confirmation, and reset
- Add reconnect snapshots for the player's own fleet and shots
- Keep opponent layouts permanently private

Do not copy the upstream synchronous procedural loop into the host or client.

## Tic-Tac-Toe

The cli-games implementation includes PvP/PvE flow and minimax, but relies on
global mutable state and a synchronous prompt/redraw loop. That structure is not
suitable for a server-authoritative host with reconnects and independent clients.

coord should keep its current rules engine. If a computer opponent is added,
implement a pure strategy interface such as:

```text
ITicTacToeStrategy
  ChooseMove(board, player)
```

The strategy should receive an immutable snapshot, use no global state, and be
covered by deterministic tests. The upstream minimax algorithm can guide the
implementation without importing its global-state design.

## Connect Four

The console-connect-4 implementation is a useful reference for:

- Seven-column/six-row presentation
- Box-drawing characters
- Color-coded pieces
- Explicit column controls
- Alpha-beta minimax
- PvP/PvE flow

coord already has the correct server-side PvP rules and gravity behavior. The
recommended work is:

- Reimplement the board presentation using Terminal.Gui
- Keep fixed-width cells and coordinate labels
- Add a pure C# strategy if PvE is wanted
- Keep AI evaluation separate from the host protocol
- Add deterministic strategy tests

Do not translate the Python object graph or terminal loop wholesale.

## Other repositories

`arasgungore/console-games` is not a direct source for the current games. Its
use of `getch`, `system("cls")`, and C-specific terminal behavior is a portability
warning for a cross-platform .NET application.

`avdaredevil/PowerSneks` is useful as a conceptual reference for serializable
state, ticks, multiple entities, bots, and save/restore. Its socket layer is
explicitly unfinished, so it should not be used as coord's networking basis.

## Integration gaps to prioritize

The main remaining work is lifecycle and presentation rather than basic win
detection:

1. Reconnect snapshots for public and caller-private state
2. Battleship private-state restoration after reconnect
3. Standard Battleship fleet metadata and sunk-ship reporting
4. Consistent, polished board rendering and input
5. Optional pure C# AI strategies for Tic-Tac-Toe and Connect Four
6. Better separation between state-to-view models and Terminal.Gui controls

Every action should be validated by the host against the player identity,
selected activity, phase, and current turn. The host should then broadcast a
public snapshot and send any caller-specific private snapshot.

## Recommended roadmap

1. Keep the current four rule engines and their tests.
2. Extract reusable board layout, coordinate, and input helpers.
3. Improve Battleship fleet metadata and placement workflow.
4. Add reconnect/public/private snapshot replay.
5. Reimplement the strongest Connect Four and Wordle-style UI ideas in C#.
6. Add pure AI strategies only after the multiplayer lifecycle is stable.
7. Add a new Wordle-style activity separately rather than changing Word Duel's
   rules.

This avoids importing unrelated runtimes while still benefiting from the best
ideas in the existing terminal-game ecosystem.
