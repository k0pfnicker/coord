using Coord.Client;
using Coord.Config;
using Coord.Host;
using Coord.Games;
using System.Text.Json;
using Terminal.Gui;

namespace Coord.Application;

internal static class TerminalUi
{
    public static void RunHost(HostServer host)
    {
        Init(host.Config.Ui);
        var window = new Window($"coord host - room {host.RoomCode}") { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
        var overview = new Label("") { X = 1, Y = 1, Width = Dim.Fill(2) };
        var status = new Label("Choose an activity, configure it, then start it.") { X = 1, Y = 3, Width = Dim.Fill(2) };
        var category = new TextField("people") { X = 14, Y = 6, Width = 24, Visible = false };
        var categoryLabel = new Label("Category:") { X = 1, Y = 6, Visible = false };
        var state = new TextView { X = 1, Y = 9, Width = Dim.Fill(2), Height = Dim.Fill(7), ReadOnly = true };
        var start = new Button("Configure / start") { X = 1, Y = 7, Visible = false };
        var pause = new Button("Pause") { X = 22, Y = 7, Visible = false };
        var resume = new Button("Resume") { X = 32, Y = 7, Visible = false };
        var skip = new Button("Skip turn") { X = 43, Y = 7, Visible = false };
        var abort = new Button("Abort / end") { X = 56, Y = 7, Visible = false };
        var reset = new Button("Reset") { X = 69, Y = 7, Visible = false };
        window.Add(overview, status, categoryLabel, category, start, pause, resume, skip, abort, reset, state);
        var games = host.AvailableGames;
        for (var i = 0; i < games.Count; i++)
        {
            var game = games[i];
            var button = new Button($"{game.Id} - {game.Name}") { X = 2, Y = 10 + i };
            button.Clicked += async () =>
            {
                await host.SelectGameAsync(game.Id);
                status.Text = $"Selected {game.Name}; configure/start when ready.";
                var who = game.Id == "who-am-i";
                category.Visible = categoryLabel.Visible = who;
                start.Visible = true;
                pause.Visible = resume.Visible = skip.Visible = abort.Visible = reset.Visible = false;
                UpdateHost(overview, state, host);
            };
            window.Add(button);
        }
        start.Clicked += async () =>
        {
            status.Text = await host.StartSelectedGameAsync(category.Text?.ToString() ?? "people");
            var controls = host.SelectedGame == "who-am-i";
            pause.Visible = resume.Visible = skip.Visible = controls;
            abort.Visible = reset.Visible = host.SelectedGame is not null;
            UpdateHost(overview, state, host);
        };
        pause.Clicked += async () => { status.Text = await host.ControlSelectedGameAsync("pause"); UpdateHost(overview, state, host); };
        resume.Clicked += async () => { status.Text = await host.ControlSelectedGameAsync("resume"); UpdateHost(overview, state, host); };
        skip.Clicked += async () => { status.Text = await host.ControlSelectedGameAsync("skip"); UpdateHost(overview, state, host); };
        abort.Clicked += async () => { status.Text = await host.ControlSelectedGameAsync("abort"); UpdateHost(overview, state, host); };
        reset.Clicked += async () => { status.Text = await host.ControlSelectedGameAsync("reset"); UpdateHost(overview, state, host); };
        var timer = new System.Timers.Timer(300);
        timer.Elapsed += (_, _) => Terminal.Gui.Application.MainLoop.Invoke(() => UpdateHost(overview, state, host, status));
        Terminal.Gui.Application.Top.Add(window); timer.Start(); Terminal.Gui.Application.Run(); timer.Stop(); Terminal.Gui.Application.Shutdown();
    }

    public static void RunClient(CoordClient client)
    {
        Init(client.Config.Ui);
        var window = new Window("coord client") { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
        var status = new Label("Connecting...") { X = 1, Y = 1, Width = Dim.Fill(2) };
        var feedback = new Label("") { X = 1, Y = 2, Width = Dim.Fill(2) };
        var view = new TextView { X = 1, Y = 17, Width = Dim.Fill(2), Height = Dim.Fill(2), ReadOnly = true };
        var word = new TextField("") { X = 1, Y = 4, Width = 24, Visible = false };
        var submit = new Button("Submit") { X = 26, Y = 4, Visible = false };
        var mystery = new TextField("") { X = 1, Y = 4, Width = 40, Visible = false };
        var act = new Button("Act") { X = 42, Y = 4, Visible = false };
        var rotate = new Button("Horizontal") { X = 1, Y = 4, Visible = false };
        var place = new Button("Place fleet") { X = 16, Y = 4, Visible = false };
        var ttt = new Button[9];
        var connect = new Button[7];
        var battle = new Button[100];
        var ships = new List<ShipLayout>();
        var orientation = "horizontal";
        var selected = new BoardCoordinate(0, 0);
        window.Add(status, feedback, view, word, submit, mystery, act, rotate, place);
        for (var i = 0; i < 9; i++)
        {
            var row = i / 3; var column = i % 3;
            var button = ttt[i] = new Button(" ") { X = 2 + column * 6, Y = 4 + row * 2, Width = 5, Visible = false };
            button.Clicked += () => Send(client, "tic-tac-toe", "move", new { row, column }, feedback);
            window.Add(button);
        }
        for (var column = 0; column < 7; column++)
        {
            var button = connect[column] = new Button($"{column + 1}") { X = 2 + column * 6, Y = 4, Width = 5, Visible = false };
            var selectedColumn = column;
            button.Clicked += () => Send(client, "connect-four", "drop", new { column = selectedColumn }, feedback);
            window.Add(button);
        }
        for (var row = 0; row < 10; row++)
            for (var column = 0; column < 10; column++)
            {
                var button = battle[row * 10 + column] = new Button(" ")
                {
                    X = 7 + column * 4,
                    Y = 6 + row,
                    Width = 3,
                    Visible = false
                };
                var selectedRow = row; var selectedColumn = column;
                button.Clicked += () =>
                {
                    selected = new BoardCoordinate(selectedRow, selectedColumn);
                    var phase = client.GenericGameState?.Payload.TryGetProperty("phase", out var phaseValue) == true
                        ? phaseValue.GetString() : null;
                    if (phase == "active")
                        Send(client, "battleship", "fire", new { row = selectedRow, column = selectedColumn }, feedback);
                    else feedback.Text = $"Selected {((char)('A' + selectedColumn))}{selectedRow + 1}; choose orientation and place fleet.";
                };
                window.Add(button);
            }
        rotate.Clicked += () => { orientation = orientation == "horizontal" ? "vertical" : "horizontal"; rotate.Text = orientation; };
        place.Clicked += () =>
        {
            var newShips = CreateFleet(selected, orientation, ships);
            if (newShips is null) { feedback.Text = "That ship does not fit or overlaps another ship."; return; }
            ships.Add(newShips);
            if (ships.Count == 5)
            {
                Send(client, "battleship", "layout", new { ships }, feedback);
                feedback.Text = "Fleet submitted; waiting for the other player.";
            }
            else feedback.Text = $"Placed {newShips.Name}; select a cell for the next ship ({ships.Count}/5).";
        };
        submit.Clicked += () => Send(client, "word-duel", "submit", new { word = word.Text?.ToString() ?? "" }, feedback);
        act.Clicked += () => Send(client, "solo-mystery", "act", new { text = mystery.Text?.ToString() ?? "" }, feedback);
        var timer = new System.Timers.Timer(250);
        timer.Elapsed += (_, _) => Terminal.Gui.Application.MainLoop.Invoke(() =>
        {
            try
            {
                status.Text = client.Admission is { Accepted: false } a ? $"Admission rejected: {a.Reason}" :
                    client.Lobby is { } lobby ? $"Room {lobby.RoomCode}; players: {lobby.Players.Count}" : "Connecting...";
                feedback.Text = client.LastGameResult ?? "";
                SetClientControls(client.SelectedGame, ttt, connect, battle, word, submit, mystery, act, rotate, place);
                view.Text = client.IsWaitingForGame ? "Waiting for the host to choose an activity." :
                    client.Game is { } game ? GameUiRenderer.Render(game, client.GenericGameState, client.PrivateGame) :
                    GameUiRenderer.Render(null, client.GenericGameState, client.PrivateGame);
            }
            catch (Exception ex)
            {
                feedback.Text = UiError("Game view refresh failed", ex);
            }
        });
        Terminal.Gui.Application.Top.Add(window); timer.Start(); Terminal.Gui.Application.Run(); timer.Stop(); Terminal.Gui.Application.Shutdown();
    }

    private static void SetClientControls(string? game, Button[] ttt, Button[] connect, Button[] battle,
        TextField word, Button submit, TextField mystery, Button act, Button rotate, Button place)
    {
        var tic = game == "tic-tac-toe"; var four = game == "connect-four"; var ships = game == "battleship";
        foreach (var button in ttt) button.Visible = tic;
        foreach (var button in connect) button.Visible = four;
        foreach (var button in battle) button.Visible = ships;
        word.Visible = submit.Visible = game == "word-duel";
        mystery.Visible = act.Visible = game == "solo-mystery";
        rotate.Visible = place.Visible = ships;
    }

    private static void Send(CoordClient client, string game, string action, object payload, Label feedback)
    {
        try
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload));
            client.GameActionAsync(game, action, document.RootElement.Clone()).GetAwaiter().GetResult();
        }
        catch (Exception ex) { feedback.Text = ex.Message; }
    }

    private static ShipLayout? CreateFleet(BoardCoordinate start, string orientation, IReadOnlyList<ShipLayout> placed)
    {
        var sizes = new[] { 5, 4, 3, 3, 2 };
        if (placed.Count >= sizes.Length) return null;
        var cells = Enumerable.Range(0, sizes[placed.Count]).Select(i =>
            orientation == "horizontal" ? new BoardCoordinate(start.Row, start.Column + i) :
            new BoardCoordinate(start.Row + i, start.Column)).ToArray();
        if (cells.Any(c => c.Row is < 0 or >= 10 || c.Column is < 0 or >= 10) ||
            cells.Any(c => placed.SelectMany(s => s.Cells).Contains(c))) return null;
        return new ShipLayout($"Ship {placed.Count + 1}", cells);
    }

    private static void UpdateHost(Label overview, TextView state, HostServer host, Label? status = null)
    {
        try
        {
            overview.Text = $"Selected: {host.SelectedGame ?? "none"} | Players: {host.Game.State.Players.Count} | " +
                $"phase: {host.CurrentPhase} | turn: {host.CurrentTurn ?? "-"}";
            state.Text = host.RenderSelectedState();
        }
        catch (Exception ex)
        {
            var message = UiError("Host view refresh failed", ex);
            state.Text = message;
            if (status is not null) status.Text = message;
        }
    }

    private static string UiError(string prefix, Exception exception)
    {
        var detail = exception.Message.ReplaceLineEndings(" ").Trim();
        if (detail.Length > 180) detail = detail[..180] + "…";
        return $"{prefix}: {detail}";
    }

    private static void Init(UiConfig? config)
    {
        var ui = config ?? UiConfig.Default;
        Console.BackgroundColor = ParseConsoleColor(ui.BackgroundColor);
        Console.ForegroundColor = ParseConsoleColor(ui.ForegroundColor);
        Terminal.Gui.Application.Init();
        var scheme = new ColorScheme
        {
            Normal = Terminal.Gui.Application.Driver.MakeAttribute(Parse(ui.ForegroundColor), Parse(ui.BackgroundColor)),
            Focus = Terminal.Gui.Application.Driver.MakeAttribute(Parse(ui.ForegroundColor), Parse(ui.BackgroundColor)),
            HotNormal = Terminal.Gui.Application.Driver.MakeAttribute(Parse(ui.AccentColor ?? ui.ForegroundColor), Parse(ui.BackgroundColor)),
            HotFocus = Terminal.Gui.Application.Driver.MakeAttribute(Parse(ui.AccentColor ?? ui.ForegroundColor), Parse(ui.BackgroundColor))
        };
        Colors.Base = scheme;
    }

    private static ConsoleColor ParseConsoleColor(string name) =>
        Enum.TryParse<ConsoleColor>(name, true, out var value) ? value : ConsoleColor.Gray;

    private static Color Parse(string name) => Enum.TryParse<ConsoleColor>(name, true, out var value)
        ? value switch
        {
            ConsoleColor.DarkYellow => Color.Brown,
            ConsoleColor.DarkGray => Color.DarkGray,
            ConsoleColor.Gray => Color.Gray,
            ConsoleColor.White => Color.White,
            ConsoleColor.Black => Color.Black,
            ConsoleColor.Blue => Color.Blue,
            ConsoleColor.DarkBlue => Color.Blue,
            ConsoleColor.Cyan => Color.Cyan,
            ConsoleColor.DarkCyan => Color.Cyan,
            ConsoleColor.Green => Color.Green,
            ConsoleColor.DarkGreen => Color.Green,
            ConsoleColor.Magenta => Color.Magenta,
            ConsoleColor.DarkMagenta => Color.Magenta,
            ConsoleColor.Red => Color.Red,
            ConsoleColor.DarkRed => Color.Red,
            ConsoleColor.Yellow => Color.Brown,
            _ => Color.Gray
        }
        : Color.Gray;
}
