using System.Text.Json.Nodes;
using System.Security.Cryptography;
using Almuerzo.Web.Models;

namespace Almuerzo.Web.Services;

public record RushDrop(int Id, int AtMs, int Lane, int Food, bool Burnt);
public partial class LunchService
{
    public static RushDrop[] RushDrops(int seed, int level, int duration)
    {
        uint state = (uint)seed;
        int Next(int max) { state = unchecked(1664525 * state + 1013904223); return (int)(state % max); }
        var drops = new List<RushDrop>();
        var interval = Math.Max(620, 1250 - level * 85);
        for (int t = 3200; t < duration * 1000 - 500; t += interval)
            drops.Add(new(drops.Count, t, Next(4), Next(6), Next(100) < 14 + level * 2));
        return drops.ToArray();
    }
    private async Task<object> StartRush(AppUser actor)
    {
        Require(actor.RegionId == db.RegionId, "Selecciona tu regional para jugar.");
        var saved = Obj(actor.GameJson); var game = Game(actor);
        var activeUntil = DateTimeOffset.TryParse(Str(saved, "rushUntil"), out var until) ? until : DateTimeOffset.MinValue;
        Require(activeUntil <= rules.Now, "Ya tienes una partida en curso. Espera a que termine antes de iniciar otra.");
        var remaining = Num(game, "remainingSeconds"); Require(remaining > 0, "Tiempo diario agotado.");
        var duration = Math.Min(45, remaining); var seed = RandomNumberGenerator.GetInt32(1, int.MaxValue); var level = Math.Clamp(Num(game, "score") / 300 + 1, 1, 7);
        game["usedSeconds"] = Num(game, "usedSeconds") + duration; game["remainingSeconds"] = remaining - duration;
        game["rushId"] = Guid.NewGuid().ToString("N"); game["rushSeed"] = seed; game["rushLevel"] = level;
        game["rushStarted"] = rules.Now.ToString("O"); game["rushUntil"] = rules.Now.AddSeconds(duration).ToString("O"); game["rushDuration"] = duration;
        game["rushCompleted"] = false; game["gameVersion"] = "rush-v1";
        actor.GameJson = game.ToJsonString(); await db.SaveChangesAsync();
        return new { ok = true, id = Str(game, "rushId"), duration, level, fallMs = Math.Max(1400, 2900 - level * 160), drops = RushDrops(seed, level, duration), state = Game(actor) };
    }
    private Task<object> FinishRush(AppUser actor, JsonNode p)
    {
        Require(actor.RegionId == db.RegionId, "Selecciona tu regional para jugar.");
        var saved = Obj(actor.GameJson);
        Require(Str(saved, "rushId") == Str(p, "id") && Str(saved, "rushCompleted") == "false" && Str(saved, "dayKey") == rules.Today, "La partida no está vigente o ya fue guardada.");
        Require(DateTimeOffset.TryParse(Str(saved, "rushStarted"), out var started), "Partida inválida.");
        var duration = Num(saved, "rushDuration");
        var elapsed = Math.Clamp((int)(rules.Now - started).TotalMilliseconds, 0, duration * 1000);
        Require(p["moves"] is JsonArray { Count: <= 450 }, "Movimientos inválidos.");
        var moves = new List<(int t, int lane)>(); var previous = -1;
        foreach (var move in (JsonArray)p["moves"]!) {
            var t = Num(move, "t", -1); var lane = Num(move, "lane", -1);
            Require(t >= 0 && t > previous && t <= elapsed + 250 && lane is >= 0 and <= 3, "Secuencia de juego inválida.");
            moves.Add((t, lane)); previous = t;
        }
        int score = Num(saved, "score"), hits = Num(saved, "hits"), misses = Num(saved, "misses"), streak = 0, best = Num(saved, "bestStreak"), delta = 0;
        foreach (var drop in RushDrops(Num(saved, "rushSeed"), Num(saved, "rushLevel"), duration).Where(x => x.AtMs <= elapsed)) {
            var lane = moves.LastOrDefault(x => x.t <= drop.AtMs, (t: 0, lane: 1)).lane;
            if (lane == drop.Lane && !drop.Burnt) { hits++; streak++; delta += 10 + Math.Min(5, streak / 5); best = Math.Max(best, streak); }
            else if (lane == drop.Lane || !drop.Burnt) { misses++; streak = 0; if (drop.Burnt) delta -= 8; }
        }
        saved["score"] = Math.Clamp(score + delta, 0, 999999); saved["hits"] = hits; saved["misses"] = misses; saved["streak"] = streak; saved["bestStreak"] = best;
        saved["rushCompleted"] = true; actor.GameJson = saved.ToJsonString();
        return Task.FromResult<object>(new { ok = true, state = Game(actor), earned = delta });
    }
}
