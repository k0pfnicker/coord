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
        var window = new Window($"Coord host - room {host.RoomCode}") { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
        var category = new TextField("people") { X = 14, Y = 1, Width = 24 };
        var status = new Label("Configuring; press Start when players are ready.") { X = 1, Y = 3 };
        var game = new Label("Players: 0; turn: -") { X = 1, Y = 4 };
        var start = new Button("Start") { X = 1, Y = 6 };
        var pause = new Button("Pause") { X = 12, Y = 6 };
        var resume = new Button("Resume") { X = 23, Y = 6 };
        var skip = new Button("Skip turn") { X = 34, Y = 6 };
        window.Add(new Label("Category:") { X = 1, Y = 1 }, category, status, game, start, pause, resume, skip);
        start.Clicked += async () =>
        {
            host.Game.Configure(category.Text?.ToString() ?? string.Empty);
            var result = await host.Game.StartAsync();
            status.Text = result.Reason;
            UpdateHostGameLabel(game, host);
        };
        pause.Clicked += () =>
        {
            status.Text = host.Game.Pause().Reason;
            UpdateHostGameLabel(game, host);
        };
        resume.Clicked += () =>
        {
            status.Text = host.Game.Resume().Reason;
            UpdateHostGameLabel(game, host);
        };
        skip.Clicked += () =>
        {
            var current = host.Game.State.CurrentPlayerId;
            status.Text = current is null ? "No current player." : host.Game.Skip(current).Reason;
            UpdateHostGameLabel(game, host);
        };
        var timer = new System.Timers.Timer(500);
        timer.Elapsed += (_, _) => Terminal.Gui.Application.MainLoop.Invoke(() => UpdateHostGameLabel(game, host));
        Terminal.Gui.Application.Top.Add(window);
        timer.Start();
        Terminal.Gui.Application.Run();
        timer.Stop();
        Terminal.Gui.Application.Shutdown();
    }

    public static void RunClient(CoordClient client)
    {
        Terminal.Gui.Application.Init();
        var window = new Window("Coord client") { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
        var status = new Label("Connecting...") { X = 1, Y = 1 };
        var game = new Label("Who Am I? waiting for host") { X = 1, Y = 3, Width = Dim.Fill(2), Height = 8 };
        window.Add(status);
        window.Add(game);
        Terminal.Gui.Application.Top.Add(window);
        var timer = new System.Timers.Timer(250);
        timer.Elapsed += (_, _) =>
        {
            var state = client.Game;
            if (state is null) return;
            var history = string.Join("\n", state.History.TakeLast(6).Select(FormatHistory));
            Terminal.Gui.Application.MainLoop.Invoke(() =>
                game.Text = $"Who Am I? {state.Phase}; turn: {state.CurrentPlayerId ?? "-"}\n" +
                    history);
        };
        timer.Start();
        Terminal.Gui.Application.Run();
        timer.Stop();
        Terminal.Gui.Application.Shutdown();
    }

    private static void UpdateHostGameLabel(Label label, HostServer host)
    {
        var state = host.Game.State;
        label.Text = $"Players: {state.Players.Count}; turn: {state.CurrentPlayerId ?? "-"}; " +
            $"phase: {state.Phase}";
    }

    private static string FormatHistory(GameHistoryItem item) =>
        $"{item.PlayerId}: {item.Text} [{item.Response ?? "pending"}]";
}
