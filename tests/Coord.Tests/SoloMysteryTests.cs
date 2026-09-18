using Coord.Games;
using Xunit;

namespace Coord.Tests;

public sealed class SoloMysteryTests
{
    [Fact]
    public async Task AdmitsExactlyOneAndHasExplicitTransitions()
    {
        var game = new SoloMysteryGame(new DeterministicMysteryProvider());
        Assert.True(game.AddPlayer("one"));
        Assert.False(game.AddPlayer("two"));
        Assert.True((await game.StartAsync()).Accepted);
        Assert.Equal(SoloMysteryPhase.Active, game.State.Phase);
        Assert.False((await game.SubmitAsync("two", "inspect")).Accepted);
        Assert.True((await game.SubmitAsync("one", "use the key")).Accepted);
        Assert.True((await game.SubmitAsync("one", "open the clock")).Accepted);
        Assert.Equal(SoloMysteryPhase.Won, game.State.Phase);
    }

    [Fact]
    public async Task PromptAndPublicStateNeverExposeSolution()
    {
        const string secret = "very secret answer";
        var prompt = MysteryPromptBuilder.Build("premise", secret, "look", []);
        Assert.Contains(secret, prompt); // server-side prompt only
        var game = new SoloMysteryGame(new DeterministicMysteryProvider(
            ["""{"scene":"safe","clue":"look closer","status":"continue"}"""]), solution: secret);
        game.AddPlayer("one");
        await game.StartAsync();
        Assert.DoesNotContain(secret, System.Text.Json.JsonSerializer.Serialize(game.State));
    }

    [Fact]
    public async Task LeakingProviderFailsWithoutPublishingOutput()
    {
        var game = new SoloMysteryGame(new DeterministicMysteryProvider(
            ["""{"scene":"very secret answer","clue":"oops","status":"continue"}"""]),
            solution: "very secret answer");
        game.AddPlayer("one");
        var result = await game.StartAsync();
        Assert.False(result.Accepted);
        Assert.Equal(SoloMysteryPhase.Aborted, game.State.Phase);
        Assert.DoesNotContain("very secret answer", System.Text.Json.JsonSerializer.Serialize(game.State));
    }

    [Fact]
    public async Task ProviderTimeoutAbortsWithoutPropagating()
    {
        var game = new SoloMysteryGame(new TimeoutProvider());
        game.AddPlayer("one");
        var result = await game.StartAsync();
        Assert.False(result.Accepted);
        Assert.Equal(SoloMysteryPhase.Aborted, game.State.Phase);
        Assert.DoesNotContain("secret", System.Text.Json.JsonSerializer.Serialize(game.State));
    }

    private sealed class TimeoutProvider : ISoloMysteryProvider
    {
        public Task<string> NextSceneAsync(string prompt, CancellationToken cancellationToken = default) =>
            Task.FromException<string>(new OperationCanceledException("timeout"));
    }
}
