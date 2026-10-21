using Almuerzo.Web.Services;

namespace Almuerzo.Tests;

public class PotDifficultyTests
{
    [Theory]
    [InlineData(0, 1, 3135)]
    [InlineData(499, 1, 3135)]
    [InlineData(500, 2, 2836)]
    [InlineData(999, 2, 2836)]
    [InlineData(1000, 3, 2537)]
    [InlineData(1499, 3, 2537)]
    [InlineData(1500, 4, 2238)]
    [InlineData(1999, 4, 2238)]
    [InlineData(2000, 5, 1938)]
    [InlineData(2499, 5, 1938)]
    [InlineData(2500, 6, 1639)]
    [InlineData(2999, 6, 1639)]
    [InlineData(3000, 7, 1340)]
    [InlineData(3500, 7, 1340)]
    [InlineData(3999, 7, 1340)]
    [InlineData(4000, 8, 1040)]
    [InlineData(999999, 8, 1040)]
    public void MonthlyScoreSetsTheExactLevelAndSpeedIncludingTheFinalThreshold(int score, int level, int fall)
    {
        var sim = new PotSimulation(1, score);
        Assert.Equal(level, sim.Level); Assert.Equal(fall, sim.FallMilliseconds);
    }

    [Fact] public void CrossingFourThousandImmediatelyUsesMaximumSpeedAndLosingPointsCanLowerIt()
    {
        var sim = new PotSimulation(1, 3999);
        sim.Drops.Add(new() { Kind = "food", Lane = 1, Progress = 7550 }); sim.Advance(0, []);
        Assert.Equal(4009, sim.Score); Assert.Equal(8, sim.Level); Assert.Equal(1040, sim.FallMilliseconds);
        sim.Drops.Add(new() { Kind = "burnt", Lane = 1, Progress = 7550 }); sim.Advance(20, []);
        Assert.Equal(3997, sim.Score); Assert.Equal(7, sim.Level);
    }

    [Fact] public async Task StaleBrowserRulesCannotStartOrOverwriteASession()
    {
        await using var app = new AppFactory(); await app.Seed(); using var client = app.Client(); await app.Login(client, "user@infotep.gob.do");
        Assert.False((await app.Rpc(client, "apiStartLunchRush"))["ok"]!.GetValue<bool>());
        Assert.False((await app.Rpc(client, "apiStartLunchRush", "pot-v3"))["ok"]!.GetValue<bool>());
        var start = await app.Rpc(client, "apiStartLunchRush", "pot-v4"); Assert.True(start["ok"]!.GetValue<bool>());
        Assert.Equal(900, start["duration"]!.GetValue<int>());
    }
}
