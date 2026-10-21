using System.Text.Json.Nodes;
using Almuerzo.Web.Data;
using Almuerzo.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Almuerzo.Tests;

public class PotPenaltiesTests
{
    private static void Drop(PotSimulation sim, string kind, int lane = 1) => sim.Drops.Add(new() { Id = 9999, Kind = kind, Lane = lane, Progress = 7550 });

    [Fact] public void NonconsecutiveNegativesCauseIncreasingLossAndTimeWithoutScoringDuringWait()
    {
        var sim = new PotSimulation(1, 1000);
        foreach (var (t, kind) in new[] { (0, "burnt"), (20, "food"), (40, "burnt"), (60, "food"), (80, "burnt") }) { Drop(sim, kind); sim.Advance(t, []); }
        Assert.Equal(954, sim.Score); Assert.Equal(3, sim.NegativeHits); Assert.Equal(3080, sim.PauseUntil); Assert.Empty(sim.Drops);
        sim.Advance(3060, []); Assert.Equal(954, sim.Score); Assert.Empty(sim.Drops);
        sim.Advance(3080, []);
        foreach (var t in new[] { 3100, 3120, 3140 }) { Drop(sim, "burnt"); sim.Advance(t, []); }
        Assert.Equal(858, sim.Score); Assert.Equal(6, sim.NegativeHits); Assert.Equal(9140, sim.PauseUntil); Assert.Equal(9, sim.PenaltySeconds);
    }
    [Fact] public void ShieldAndFeastMakeNegativesCompletelyHarmlessIncludingTheDailyCounter()
    {
        foreach (var effect in new[] { "shield", "feast" }) {
            var sim = new PotSimulation(1, 300, 2);
            Drop(sim, "food"); sim.Advance(0, []); Drop(sim, effect); sim.Advance(20, []);
            foreach (var t in new[] { 40, 60, 80 }) { Drop(sim, "burnt"); sim.Advance(t, []); }
            Assert.Equal(310, sim.Score); Assert.Equal(1, sim.Streak); Assert.Equal(2, sim.NegativeHits); Assert.Equal(0, sim.Misses); Assert.Equal(0, sim.PauseUntil);
            sim.Advance(10040, []); sim.Drops.Clear(); var score = sim.Score; var negatives = sim.NegativeHits;
            Drop(sim, "burnt"); sim.Advance(10060, []);
            Assert.Equal(negatives + 1, sim.NegativeHits); Assert.True(sim.Score <= score - 12); Assert.Equal(0, sim.Streak);
        }
    }
    [Theory]
    [InlineData(2, 3)]
    [InlineData(29, 30)]
    public async Task StoppingReloggingOrStartingAgainCannotSkipPenaltyTimeOrResetTheDailyCount(int previousNegatives, int expectedPauseSeconds)
    {
        await using var app = new AppFactory(); await app.Seed(); using var c = app.Client(); await app.Login(c, "user@infotep.gob.do");
        using (var scope = app.Services.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Users.SingleAsync(x => x.Email == "user@infotep.gob.do")).GameJson = new JsonObject {
                ["gameVersion"] = "pot-v3", ["dayKey"] = "2026-10-09", ["monthKey"] = "2026-10", ["score"] = 1000,
                ["usedSeconds"] = 200, ["negativeHits"] = previousNegatives
            }.ToJsonString(); await db.SaveChangesAsync();
        }
        var start = await app.Rpc(c, "apiStartLunchRush"); Assert.True(start["ok"]!.GetValue<bool>());
        var id = start["id"]!.ToString(); var begin = app.Clock.Current;
        using (var scope = app.Services.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var u = await db.Users.SingleAsync(x => x.Email == "user@infotep.gob.do");
            var game = LunchService.Obj(u.GameJson); game["rushSeed"] = 1; u.GameJson = game.ToJsonString(); await db.SaveChangesAsync();
        }
        var sim = new PotSimulation(1, 1000, previousNegatives);
        while (sim.PauseUntil == 0 && sim.Time < 180000) sim.Advance(sim.Time + 20, []);
        Assert.True(sim.PauseUntil > sim.Time); var end = sim.Time;
        for (int elapsed = 10000; elapsed < end; elapsed += 10000) {
            app.Clock.Current = begin.AddMilliseconds(elapsed);
            Assert.True((await app.Rpc(c, "apiCheckpointLunchRush", new { id, elapsedMs = elapsed, moves = Array.Empty<object>() }))["ok"]!.GetValue<bool>());
        }
        app.Clock.Current = begin.AddMilliseconds(end);
        var finish = await app.Rpc(c, "apiFinishLunchRush", new { id, elapsedMs = end, moves = Array.Empty<object>(), negativeHits = 0, pauseUntil = 0 });
        Assert.True(finish["ok"]!.GetValue<bool>()); Assert.Equal(previousNegatives + 1, finish["state"]!["negativeHits"]!.GetValue<int>());
        Assert.Equal(expectedPauseSeconds * 1000, finish["state"]!["cooldownRemainingMs"]!.GetValue<int>());
        Assert.Equal(sim.Score, finish["state"]!["score"]!.GetValue<int>());
        Assert.False((await app.Rpc(c, "apiStartLunchRush"))["ok"]!.GetValue<bool>());
        app.Clock.Current = app.Clock.Current.AddSeconds(1);
        using var again = app.Client(); await app.Login(again, "user@infotep.gob.do");
        var init = await app.Rpc(again, "apiGetInitData");
        Assert.Equal(700 - (end + 999) / 1000 - 1, init["chefGame"]!["remainingSeconds"]!.GetValue<int>());
        Assert.False((await app.Rpc(again, "apiStartLunchRush"))["ok"]!.GetValue<bool>());
        var retry = await app.Rpc(again, "apiFinishLunchRush", new { id, elapsedMs = end, moves = Array.Empty<object>() });
        Assert.Equal(sim.Score, retry["state"]!["score"]!.GetValue<int>()); Assert.Equal(previousNegatives + 1, retry["state"]!["negativeHits"]!.GetValue<int>());
        app.Clock.Current = begin.AddMilliseconds(sim.PauseUntil + 20);
        var resume = await app.Rpc(again, "apiStartLunchRush"); Assert.True(resume["ok"]!.GetValue<bool>());
        Assert.Equal(previousNegatives + 1, resume["baseNegatives"]!.GetValue<int>());
        Assert.Equal(700 - (sim.PauseUntil + 999) / 1000, resume["duration"]!.GetValue<int>());
        Assert.True((await app.Rpc(again, "apiFinishLunchRush", new { id = resume["id"]!.ToString(), elapsedMs = 0, moves = Array.Empty<object>() }))["ok"]!.GetValue<bool>());
        app.Clock.Current = app.Clock.Current.AddDays(1); using var nextDay = app.Client(); await app.Login(nextDay, "user@infotep.gob.do");
        var next = await app.Rpc(nextDay, "apiStartLunchRush"); Assert.True(next["ok"]!.GetValue<bool>());
        Assert.Equal(900, next["duration"]!.GetValue<int>()); Assert.Equal(0, next["baseNegatives"]!.GetValue<int>());
    }
}
