using Coord.Config;
using Coord.Host;
using System.Text.Json;
using Xunit;

namespace Coord.Tests;

public sealed class HostStartupTests
{
    [Fact]
    public async Task HostStartsInNeutralLobbyAndCannotStartBeforeSelection()
    {
        await using var host = new HostServer(CoordConfig.Default());

        Assert.Null(host.SelectedGame);
        Assert.Contains(host.AvailableGames, game => game.Id == "solo-mystery");
        Assert.Equal("Choose a game first.", await host.StartSelectedGameAsync());
    }

    [Fact]
    public async Task SelectingWhoAmIStillRequiresPlayersAndCategoryBeforeStart()
    {
        await using var host = new HostServer(CoordConfig.Default());

        await host.SelectGameAsync("who-am-i");

        Assert.Equal("who-am-i", host.SelectedGame);
        Assert.Equal("At least one player is required.", await host.StartSelectedGameAsync("people"));
    }

    [Fact]
    public async Task TicTacToeSelectedStateUsesJsonSafeCoordinateKeys()
    {
        await using var host = new HostServer(CoordConfig.Default());

        await host.SelectGameAsync("tic-tac-toe");
        var payload = host.SelectedStatePayload();

        Assert.Equal(JsonValueKind.Object, payload.ValueKind);
        Assert.True(payload.TryGetProperty("board", out var board));
        Assert.Equal(JsonValueKind.Object, board.ValueKind);
        Assert.DoesNotContain("BoardCoordinate", payload.GetRawText());
    }
}
