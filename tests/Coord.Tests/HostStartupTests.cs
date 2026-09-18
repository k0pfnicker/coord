using Coord.Config;
using Coord.Host;
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
}
