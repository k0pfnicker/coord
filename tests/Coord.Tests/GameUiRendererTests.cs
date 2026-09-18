using System.Text.Json;
using Coord.Application;
using Coord.Protocol;
using Xunit;

namespace Coord.Tests;

public sealed class GameUiRendererTests
{
    [Fact]
    public void TicTacToeStateRendersBoardInsteadOfRawPayload()
    {
        using var document = JsonDocument.Parse(
            """{"phase":"active","currentPlayerId":"Ada","board":{"0,0":"Ada","1,1":"Bob"}}""");
        var text = GameUiRenderer.Render(null,
            new GameActionMessage("tic-tac-toe", "state", document.RootElement.Clone()));
        Assert.Contains("Tic-Tac-Toe", text);
        Assert.Contains("A", text);
        Assert.DoesNotContain("\"board\"", text);
    }

    [Fact]
    public void MysteryStateRendersSceneAndClue()
    {
        using var document = JsonDocument.Parse(
            """{"phase":"active","scene":"Archive","clue":"Crescent mark","history":["look"]}""");
        var text = GameUiRenderer.Render(null,
            new GameActionMessage("solo-mystery", "state", document.RootElement.Clone()));
        Assert.Contains("Archive", text);
        Assert.Contains("Crescent mark", text);
        Assert.Contains("look", text);
    }

    [Fact]
    public void BoardRenderingUsesFixedWidthBordersAndCoordinates()
    {
        using var document = JsonDocument.Parse(
            """{"phase":"active","currentPlayerId":"Ada","symbols":{"Ada":"X","Bob":"O"},"board":{"0,0":"X","2,2":"O"}}""");
        var text = GameUiRenderer.Render(null,
            new GameActionMessage("tic-tac-toe", "state", document.RootElement.Clone()));
        Assert.Contains("A   B   C", text);
        Assert.Contains("+---+---+---+", text);
        Assert.Contains(" 1 |", text);
        Assert.Contains("Players: Ada=X  Bob=O", text);
        Assert.Contains(" X ", text);
        Assert.True(text.IndexOf(" 1 |") < text.IndexOf(" 3 |"));
    }

    [Fact]
    public void BattleshipRendererDoesNotExposePublicFleetPayload()
    {
        using var document = JsonDocument.Parse(
            """{"phase":"active","ownLayout":[],"visibleShots":{"(0, 0)":"miss"}}""");
        var text = GameUiRenderer.Render(null,
            new GameActionMessage("battleship", "state", document.RootElement.Clone()));
        Assert.Contains("Opponent ships are hidden", text);
        Assert.DoesNotContain("\"ownLayout\"", text);
    }
}
