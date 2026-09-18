using Coord.Ai;
using Coord.Config;
using Coord.Research;
using Coord.Storage;
using Xunit;

namespace Coord.Tests;

public sealed class ProviderAndPersistenceTests
{
    [Fact]
    public async Task DefaultFactoryIsDeterministicAndOffline()
    {
        var provider = ProviderFactory.CreateIdentity(CoordConfig.Default().Ai);
        var first = await provider.ChooseAsync("people");
        var second = await provider.ChooseAsync("people");
        Assert.Equal(first, second);
        Assert.Throws<InvalidOperationException>(() => ProviderFactory.CreateAi(new AiConfig("web")));
    }

    [Fact]
    public void InvalidConfigurationIsRejected()
    {
        var invalid = CoordConfig.Default() with
        {
            Network = new NetworkConfig("localhost:99999", TimeSpan.FromSeconds(1),
                UseTls: false)
        };
        Assert.Throws<FormatException>(() => ConfigValidator.Validate(invalid));
    }

    [Fact]
    public async Task DossiersAndLeaderboardRoundTrip()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "round-trip-" + Guid.NewGuid().ToString("N"));
        try
        {
            var repository = new JsonResearchRepository(new FileStorage(directory));
            await repository.SaveIdentityAsync(new IdentityDossier("people", "Ada", ["A"], DateTimeOffset.UtcNow));
            await repository.UpdateStatsAsync("one", stats => stats.RecordGame(true).RecordGuess(true));
            await repository.UpdateStatsAsync("one", stats => stats.RecordQuestion());

            var reloaded = new JsonResearchRepository(new FileStorage(directory));
            var dossier = Assert.Single(await reloaded.LoadIdentitiesAsync());
            var stats = Assert.Single(await reloaded.LoadLeaderboardAsync());
            Assert.Equal("Ada", dossier.Name);
            Assert.Equal(1, stats.Wins);
            Assert.Equal(1, stats.Questions);
            Assert.Equal(1, stats.CorrectGuesses);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
