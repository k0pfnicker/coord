using Coord.Games;
using Coord.Protocol;
using System.Text.Json;
using Xunit;

namespace Coord.Tests;

public sealed class GameTests
{
    [Fact]
    public void TwoPlayerLimitAndTurnsAreEnforced()
    {
        var game = new TicTacToeGame();
        Assert.True(game.AddPlayer("a")); Assert.True(game.AddPlayer("b"));
        Assert.False(game.AddPlayer("c")); Assert.True(game.Start().Accepted);
        Assert.False(game.Move("b", new(0, 0)).Accepted);
        Assert.True(game.Move("a", new(0, 0)).Accepted);
        Assert.False(game.Move("a", new(0, 1)).Accepted);
    }

    [Fact]
    public void TicTacToeDetectsWinAndIllegalCell()
    {
        var game = new TicTacToeGame(); game.AddPlayer("a"); game.AddPlayer("b"); game.Start();
        game.Move("a", new(0, 0)); game.Move("b", new(1, 0));
        game.Move("a", new(0, 1)); game.Move("b", new(1, 1));
        Assert.True(game.Move("a", new(0, 2)).Accepted);
        Assert.Equal("won", game.State.Phase);
        Assert.False(game.Move("b", new(2, 2)).Accepted);
    }

    [Fact]
    public void BoardCoordinatesUseRowZeroAsVisualTop()
    {
        var ttt = new TicTacToeGame();
        ttt.AddPlayer("same"); ttt.AddPlayer("same-2"); ttt.Start();
        Assert.True(ttt.Move("same", new(0, 0)).Accepted);
        Assert.Equal("X", ttt.State.Board[new(0, 0)]);
        Assert.Equal("X", ttt.State.Symbols["same"]);

        var four = new ConnectFourGame();
        four.AddPlayer("same"); four.AddPlayer("same-2"); four.Start();
        Assert.True(four.Drop("same", 0).Accepted);
        Assert.Contains(new BoardCoordinate(5, 0), four.State.Board.Keys);
        Assert.Equal("●", four.State.Board[new(5, 0)]);
        Assert.NotEqual(four.State.Symbols["same"], four.State.Symbols["same-2"]);

        var battleship = new BattleshipGame();
        battleship.AddPlayer("same"); battleship.AddPlayer("same-2");
        Assert.True(battleship.SetLayout("same", [new ShipLayout("edge", [new(0, 0)])]).Accepted);
        Assert.Equal(new BoardCoordinate(0, 0), battleship.ViewFor("same").OwnLayout[0].Cells[0]);
    }

    [Fact]
    public void ConnectFourDropsAndRejectsFullColumn()
    {
        var game = new ConnectFourGame(); game.AddPlayer("a"); game.AddPlayer("b"); game.Start();
        for (var i = 0; i < 6; i++) { Assert.True(game.Drop(i % 2 == 0 ? "a" : "b", 0).Accepted); }
        Assert.False(game.Drop("a", 0).Accepted);
    }

    [Fact]
    public void WordDuelValidatesDuplicatesAndScores()
    {
        var game = new WordDuelGame(rules: new WordDuelRules("animals", 3, 8, 2));
        game.AddPlayer("a"); game.AddPlayer("b"); game.Start();
        var accepted = game.Submit("a", "cat");
        Assert.True(accepted.Accepted); Assert.Equal(2, accepted.Points);
        Assert.False(game.Submit("b", "cat").Accepted); // duplicate
        Assert.False(game.Submit("b", "x!").Accepted);
    }

    [Fact]
    public void BattleshipDoesNotExposeOpponentLayout()
    {
        var game = new BattleshipGame(); game.AddPlayer("a"); game.AddPlayer("b");
        game.SetLayout("a", [new ShipLayout("a", [new(0, 0)])]);
        game.SetLayout("b", [new ShipLayout("b", [new(4, 4)])]); game.Start();
        var view = game.ViewFor("a");
        Assert.Single(view.OwnLayout); Assert.DoesNotContain(view.OwnLayout, s => s.Name == "b");
        Assert.True(game.Fire("a", new(4, 4)).Accepted);
        Assert.DoesNotContain(new BoardCoordinate(0, 0), game.ViewFor("b").VisibleShots.Keys);
    }

    [Fact]
    public void EachGameRequiresExactlyTwoPlayers()
    {
        Assert.False(new WordDuelGame().Start().Accepted);
        var game = new ConnectFourGame();
        Assert.True(game.AddPlayer("a"));
        Assert.False(game.Start().Accepted);
        Assert.True(game.AddPlayer("b"));
        Assert.False(game.AddPlayer("c"));
    }

    [Fact]
    public void ConnectFourDetectsAHorizontalWin()
    {
        var game = new ConnectFourGame();
        game.AddPlayer("a"); game.AddPlayer("b"); game.Start();
        game.Drop("a", 0); game.Drop("b", 0);
        game.Drop("a", 1); game.Drop("b", 1);
        game.Drop("a", 2); game.Drop("b", 2);
        Assert.True(game.Drop("a", 3).Accepted);
        Assert.Equal("won", game.State.Phase);
        Assert.Equal("a", game.State.WinnerId);
    }

    [Fact]
    public void GenericGameActionRoundTripsItsPayload()
    {
        using var document = JsonDocument.Parse("""{"row":1,"column":2}""");
        var original = new GameActionMessage("tic-tac-toe", "move", document.RootElement.Clone());
        var decoded = Assert.IsType<GameActionMessage>(
            ProtocolCodec.Deserialize(ProtocolCodec.Serialize(original)));
        Assert.Equal(original.GameId, decoded.GameId);
        Assert.Equal(1, decoded.Payload.GetProperty("row").GetInt32());
        Assert.Equal(2, decoded.Payload.GetProperty("column").GetInt32());
    }
}
