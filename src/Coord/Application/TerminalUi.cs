using Coord.Client;
using Coord.Host;
using Coord.Protocol;
using Terminal.Gui;

namespace Coord.Application;

internal static class TerminalUi
{
    public static void RunHost(HostServer host)
    {
        Terminal.Gui.Application.Init();
        var window = new Window($"coord host - room {host.RoomCode}")
        { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
        var selected = new Label("Selected game: none (lobby/setup)") { X = 1, Y = 1, Width = Dim.Fill(2) };
        var overview = new Label("Players: 0 | Address: " + host.Config.Network.Address)
        { X = 1, Y = 2, Width = Dim.Fill(2) };
        var status = new Label("Choose an activity to configure it; nothing has started.")
        { X = 1, Y = 4, Width = Dim.Fill(2) };
        var category = new TextField("people") { X = 14, Y = 7, Width = 24 };
        var start = new Button("Configure / start") { X = 1, Y = 9 };
        var pause = new Button("Pause") { X = 22, Y = 9 };
        var resume = new Button("Resume") { X = 32, Y = 9 };
        var skip = new Button("Skip turn") { X = 43, Y = 9 };
        var gameControls = new Label("Select a game to reveal its setup controls.")
        { X = 1, Y = 6, Width = Dim.Fill(2) };
        var categoryLabel = new Label("Category:") { X = 1, Y = 7 };
        window.Add(selected, overview, status,
            new Label("Available games (select one):") { X = 1, Y = 12 });

        var gameButtons = new List<Button>();
        var games = host.AvailableGames;
        for (var i = 0; i < games.Count; i++)
        {
            var game = games[i];
            var button = new Button($"{game.Id} - {game.Name}") { X = 2, Y = 14 + i };
            button.Clicked += async () =>
            {
                await host.SelectGameAsync(game.Id);
                selected.Text = $"Selected game: {game.Name} ({game.Id})";
                gameControls.Text = game.Id == "who-am-i"
                    ? "Who Am I setup: choose a category, then configure/start."
                    : $"{game.Name} setup: waiting for the required players/configuration.";
                status.Text = $"Selected {game.Name}; configure it before starting.";
                gameControls.Visible = true;
                categoryLabel.Visible = true;
                categoryLabel.Visible = game.Id == "who-am-i";
                category.Visible = game.Id == "who-am-i";
                start.Visible = true;
            };
            gameButtons.Add(button);
            window.Add(button);
        }

        window.Add(gameControls, categoryLabel, category,
            start, pause, resume, skip);
        gameControls.Visible = true;
        categoryLabel.Visible = false;
        category.Visible = false;
        start.Visible = false;
        pause.Visible = false;
        resume.Visible = false;
        skip.Visible = false;
        start.Clicked += async () =>
        {
            status.Text = await host.StartSelectedGameAsync(category.Text?.ToString() ?? "people");
            if (host.SelectedGame == "who-am-i")
            {
                pause.Visible = true;
                resume.Visible = true;
                skip.Visible = true;
            }
            UpdateHostOverview(overview, host);
        };
        pause.Clicked += () =>
        {
            status.Text = host.Game.Pause().Reason;
            UpdateHostOverview(overview, host);
        };
        resume.Clicked += () =>
        {
            status.Text = host.Game.Resume().Reason;
            UpdateHostOverview(overview, host);
        };
        skip.Clicked += () =>
        {
            var current = host.Game.State.CurrentPlayerId;
            status.Text = current is null ? "No current player." : host.Game.Skip(current).Reason;
            UpdateHostOverview(overview, host);
        };

        var timer = new System.Timers.Timer(500);
        timer.Elapsed += (_, _) => Terminal.Gui.Application.MainLoop.Invoke(() =>
        {
            UpdateHostOverview(overview, host);
            selected.Text = $"Selected game: {host.SelectedGame ?? "none (lobby/setup)"}";
        });
        Terminal.Gui.Application.Top.Add(window);
        timer.Start();
        Terminal.Gui.Application.Run();
        timer.Stop();
        Terminal.Gui.Application.Shutdown();
    }

    public static void RunClient(CoordClient client)
    {
        Terminal.Gui.Application.Init();
        var window = new Window("coord client") { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
        var status = new Label("Connecting...") { X = 1, Y = 1, Width = Dim.Fill(2) };
        var game = new Label("Waiting for the host to choose an activity...")
        { X = 1, Y = 3, Width = Dim.Fill(2), Height = 8 };
        window.Add(status, game);
        Terminal.Gui.Application.Top.Add(window);
        var timer = new System.Timers.Timer(250);
        timer.Elapsed += (_, _) =>
        {
            var state = client.Game;
            var text = client.IsWaitingForGame
                ? "Waiting for the host to choose and configure a game."
                : state is null && client.GenericGameState is null
                    ? $"Selected game: {client.SelectedGame}; waiting for the host to start it."
                    : state is null
                        ? $"Selected game: {client.SelectedGame}\n{client.GenericGameState?.Payload}"
                        : $"{state.GameId} {state.Phase}; turn: {state.CurrentPlayerId ?? "-"}\n" +
                          string.Join("\n", state.History.TakeLast(6).Select(FormatHistory));
            Terminal.Gui.Application.MainLoop.Invoke(() =>
            {
                status.Text = client.Admission is { Accepted: false } admission
                    ? $"Admission rejected: {admission.Reason}"
                    : client.Lobby is { } lobby
                        ? $"Room {lobby.RoomCode}; players: {lobby.Players.Count}"
                        : "Connecting...";
                game.Text = text;
            });
        };
        timer.Start();
        Terminal.Gui.Application.Run();
        timer.Stop();
        Terminal.Gui.Application.Shutdown();
    }

    private static void UpdateHostOverview(Label label, HostServer host)
    {
        var state = host.Game.State;
        label.Text = $"Players: {state.Players.Count}; phase: {state.Phase}; " +
            $"turn: {state.CurrentPlayerId ?? "-"}";
    }

    private static string FormatHistory(GameHistoryItem item) =>
        $"{item.PlayerId}: {item.Text} [{item.Response ?? "pending"}]";
}
