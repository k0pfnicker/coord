using Coord.Games;
using Coord.Protocol;
using Xunit;

namespace Coord.Tests;

public sealed class WhoAmITests
{
    private static WhoAmIGame Game(string identity = "Ada")
    {
        var game = new WhoAmIGame(new ManualIdentityProvider(new Identity(identity, [])));
        game.AddPlayer("one");
        game.AddPlayer("two");
        Assert.True(game.Configure("people").Accepted);
        Assert.True(game.StartAsync().Result.Accepted);
        return game;
    }

    [Fact]
    public void TurnsAreStrictAndQuestionsNeedQuestionMark()
    {
        var game = Game();
        Assert.False(game.SubmitQuestion("two", "Is it human?").Accepted);
        Assert.False(game.SubmitQuestion("one", "Is it human").Accepted);
        Assert.True(game.SubmitQuestion("one", "Is it human?").Accepted);
        Assert.True(game.AnswerQuestion("one", WhoAmIAnswer.Yes).Accepted);
        Assert.Equal("two", game.State.CurrentPlayerId);
    }

    [Fact]
    public void RephraseAndUncertainRetainTurn()
    {
        var game = Game();
        game.SubmitQuestion("one", "Is it human?");
        Assert.True(game.AnswerQuestion("one", WhoAmIAnswer.Rephrase).Accepted);
        Assert.Equal("one", game.State.CurrentPlayerId);
        game.SubmitQuestion("one", "Was it born before 1900?");
        game.AnswerQuestion("one", WhoAmIAnswer.Uncertain);
        Assert.Equal("one", game.State.CurrentPlayerId);
    }

    [Fact]
    public void IncorrectGuessLosesTurnAndCorrectGuessWins()
    {
        var game = Game();
        Assert.True(game.Guess("one", "Grace").Accepted);
        Assert.Equal("two", game.State.CurrentPlayerId);
        Assert.True(game.Guess("two", "Ada").Accepted);
        Assert.Equal(WhoAmIPhase.Won, game.State.Phase);
        Assert.Equal("two", game.State.WinnerId);
    }

    [Fact]
    public void DisconnectRemovesPlayerAndAbortsWhenEmpty()
    {
        var game = Game();
        game.RemovePlayer("one");
        game.RemovePlayer("two");
        Assert.Equal(WhoAmIPhase.Aborted, game.State.Phase);
    }

    [Fact]
    public void GameMessagesRoundTripWithoutIdentity()
    {
        var message = new GameStateMessage("Active", "people", "one",
            [new GameHistoryItem("one", "Is it human?", "question", "Yes")], null, null);
        var decoded = Assert.IsType<GameStateMessage>(
            ProtocolCodec.Deserialize(ProtocolCodec.Serialize(message)));
        Assert.Equal(message.Phase, decoded.Phase);
        Assert.Equal(message.Category, decoded.Category);
        Assert.Equal(message.CurrentPlayerId, decoded.CurrentPlayerId);
        Assert.Equal(message.History, decoded.History);
        Assert.DoesNotContain("Ada", ProtocolCodec.Serialize(message));
    }
}
