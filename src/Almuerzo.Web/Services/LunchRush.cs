using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Almuerzo.Web.Models;

namespace Almuerzo.Web.Services;

public record RushMove(int T, int Lane);
public sealed class PotDrop
{
    public int Id { get; init; }
    public int Lane { get; init; }
    public int Food { get; init; }
    public string Kind { get; init; } = "food";
    public int Progress { get; set; }
}

// Integer coordinates and fixed 20 ms steps match frontend/lunch-rush.js.
// Only controls come from the browser; scoring, collision and effects are replayed here.
public sealed class PotSimulation(int seed, int baseScore, int baseNegatives = 0)
{
    private uint random = (uint)seed;
    private int moveIndex, nextSpawn = 600, serial, lastSpecial = -10000, normalCount;
    public int Time { get; private set; } = -20;
    public int Lane { get; private set; } = 1;
    public int Score { get; private set; } = Math.Clamp(baseScore, 0, 999999);
    public int Hits { get; private set; }
    public int Misses { get; private set; }
    public int Streak { get; private set; }
    public int Best { get; private set; }
    public int FrenzyUntil { get; private set; }
    public int SlowUntil { get; private set; }
    public int ShieldUntil { get; private set; }
    public int NegativeHits { get; private set; } = Math.Max(0, baseNegatives);
    public int PauseUntil { get; private set; }
    public int PenaltySeconds { get; private set; }
    public int Level => Math.Clamp(Score / 300 + 1, 1, 8);
    public List<PotDrop> Drops { get; } = [];
    private int Next(int max) { random ^= random << 13; random ^= random >> 17; random ^= random << 5; return (int)(random % (uint)max); }
    public void Advance(int elapsed, IReadOnlyList<RushMove> moves)
    {
        for (int t = Time + 20; t <= elapsed; t += 20) {
            Time = t;
            while (moveIndex < moves.Count && moves[moveIndex].T <= t) Lane = moves[moveIndex++].Lane;
            if (t < PauseUntil) continue;
            if (t >= nextSpawn) {
                string kind = "food";
                if (t - lastSpecial >= 18000 && Next(100) < 6) {
                    var special = Next(Level >= 3 ? 3 : 2);
                    kind = special == 0 ? "feast" : special == 1 ? "shield" : "slow"; lastSpecial = t;
                } else if (t >= FrenzyUntil) {
                    normalCount++; if (normalCount % 5 == 0 || Next(100) < 9) kind = "burnt";
                }
                Drops.Add(new() { Id = serial++, Lane = Next(4), Food = Next(6), Kind = kind });
                nextSpawn = t + Math.Max(480, 1150 - Level * 80);
            }
            int fall = Math.Max(1250, 3400 - Level * 265);
            if (t < SlowUntil) fall = fall * 17 / 10;
            foreach (var drop in Drops.ToArray()) {
                drop.Progress += 200000 / fall;
                // Catch on first overlap with the pot rim, with a generous contact window.
                bool caught = drop.Progress >= 7600 && drop.Progress <= 9000 && drop.Lane == Lane;
                if (caught) {
                    if (drop.Kind == "food") { Hits++; Streak++; Best = Math.Max(Best, Streak); Score = Math.Min(999999, Score + (10 + Math.Min(5, Streak / 5)) * (t < FrenzyUntil ? 3 : 1)); }
                    else if (drop.Kind == "burnt" && t >= ShieldUntil && t >= FrenzyUntil) {
                        Score = Math.Max(0, Score - 12); Misses++; Streak = 0; NegativeHits++;
                        if (NegativeHits % 3 == 0) {
                            int penalty = NegativeHits / 3;
                            Score = Math.Max(0, Score - 30 * penalty);
                            PauseUntil = t + 3000 * penalty; PenaltySeconds += 3 * penalty;
                            nextSpawn = PauseUntil + 600; Drops.Clear(); break;
                        }
                    }
                    else if (drop.Kind == "feast") FrenzyUntil = t + 10000;
                    else if (drop.Kind == "shield") ShieldUntil = t + 10000;
                    else if (drop.Kind == "slow") SlowUntil = t + 8000;
                    Drops.Remove(drop);
                } else if (drop.Progress > 9400) { if (drop.Kind == "food") { Misses++; Streak = 0; } Drops.Remove(drop); }
            }
            if (t < FrenzyUntil) Drops.RemoveAll(x => x.Kind == "burnt");
        }
    }
}

public partial class LunchService
{
    private static string MoveHash(IEnumerable<RushMove> moves) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(';', moves.Select(x => $"{x.T}:{x.Lane}")))));
    private int RushCharged(JsonNode saved)
    {
        if (!DateTimeOffset.TryParse(Str(saved, "rushStarted"), out var started)) return Num(saved, "usedSeconds");
        var elapsed = (int)Math.Clamp((long)(rules.Now - started).TotalMilliseconds, 0, Num(saved, "rushDuration") * 1000L);
        if (Str(saved, "rushCompleted") != "false") {
            if (Num(saved, "rushPauseUntil") <= Num(saved, "rushElapsed")) return Num(saved, "usedSeconds");
            elapsed = Math.Max(Num(saved, "rushElapsed"), Math.Min(elapsed, Num(saved, "rushPauseUntil")));
            return Math.Min(900, Num(saved, "rushUsedBefore") + (elapsed + 999) / 1000);
        }
        // Lost tabs consume at most the 20-second lease beyond their last checkpoint.
        elapsed = Math.Min(elapsed, Num(saved, "rushElapsed") + 20000);
        return Math.Min(900, Num(saved, "rushUsedBefore") + (elapsed + 999) / 1000);
    }
    private async Task<object> StartRush(AppUser actor)
    {
        Require(actor.RegionId == db.RegionId, "Selecciona tu regional para jugar.");
        var saved = Obj(actor.GameJson);
        var active = Str(saved, "rushCompleted") == "false" && Str(saved, "dayKey") == rules.Today && Str(saved, "gameVersion") == "pot-v3";
        if (active) {
            Require(!DateTimeOffset.TryParse(Str(saved, "rushUntil"), out var until) || until <= rules.Now, "Ya tienes una partida abierta. Detén y guarda esa partida o espera 20 segundos.");
            saved["usedSeconds"] = RushCharged(saved); saved["rushCompleted"] = true; actor.GameJson = saved.ToJsonString();
        }
        var game = Game(actor); var remaining = Num(game, "remainingSeconds"); Require(remaining > 0, "Tiempo diario agotado.");
        Require(Num(game, "cooldownRemainingMs") == 0, "La cocina sigue en pausa por los objetos negativos. El tiempo diario sigue corriendo.");
        var untilMidnight = (int)Math.Ceiling((rules.Now.Date.AddDays(1) - rules.Now.DateTime).TotalSeconds);
        var duration = Math.Min(remaining, Math.Max(1, untilMidnight)); var seed = RandomNumberGenerator.GetInt32(1, int.MaxValue);
        game["rushId"] = Guid.NewGuid().ToString("N"); game["rushSeed"] = seed; game["rushBaseScore"] = Num(game, "score");
        game["rushBaseHits"] = Num(game, "hits"); game["rushBaseMisses"] = Num(game, "misses"); game["rushBaseBest"] = Num(game, "bestStreak");
        game["rushBaseNegatives"] = Num(game, "negativeHits"); game["rushBasePenaltySeconds"] = Num(game, "penaltySeconds"); game["rushPauseUntil"] = 0;
        game["rushUsedBefore"] = Num(game, "usedSeconds"); game["rushElapsed"] = 0; game["rushPrefixHash"] = MoveHash([]);
        game["rushStarted"] = rules.Now.ToString("O"); game["rushUntil"] = rules.Now.AddSeconds(Math.Min(duration, 20)).ToString("O");
        game["rushDuration"] = duration; game["rushCompleted"] = false;
        actor.GameJson = game.ToJsonString(); await db.SaveChangesAsync();
        return new { ok = true, id = Str(game, "rushId"), seed, baseScore = Num(game, "score"), baseNegatives = Num(game, "negativeHits"), duration, state = Game(actor) };
    }
    private Task<object> FinishRush(AppUser actor, JsonNode p, bool checkpoint = false)
    {
        Require(actor.RegionId == db.RegionId, "Selecciona tu regional para jugar.");
        var saved = Obj(actor.GameJson);
        Require(Str(saved, "rushId") == Str(p, "id") && Str(saved, "gameVersion") == "pot-v3", "La partida no está vigente. Recarga la página.");
        if (!checkpoint && Str(saved, "rushCompleted") == "true") return Task.FromResult<object>(new { ok = true, state = Game(actor), earned = Num(saved, "rushEarned") });
        Require(Str(saved, "rushCompleted") == "false", "La partida ya fue guardada.");
        Require(DateTimeOffset.TryParse(Str(saved, "rushStarted"), out var started), "Partida inválida.");
        Require(DateTimeOffset.TryParse(Str(saved, "rushUntil"), out var lease), "Partida inválida.");
        var wall = Math.Max(0, (long)(rules.Now - started).TotalMilliseconds);
        int priorElapsed = Num(saved, "rushElapsed"), duration = Num(saved, "rushDuration") * 1000;
        int elapsed = Num(p, "elapsedMs", (int)Math.Min(wall, duration));
        Require(elapsed >= priorElapsed && elapsed <= duration && elapsed <= wall + 250 && elapsed <= priorElapsed + 20000 && elapsed % 20 == 0, "Tiempo de juego inválido o conexión interrumpida. Recarga para recuperar el tiempo disponible.");
        Require(!checkpoint || rules.Now <= lease.AddSeconds(2), "Se perdió la conexión de la partida. Detén y guarda.");
        Require(p["moves"] is JsonArray { Count: <= 9000 }, "Movimientos inválidos.");
        var moves = new List<RushMove>(); int previous = -100, lane = 1;
        foreach (var move in (JsonArray)p["moves"]!) {
            int t = Num(move, "t", -1), next = Num(move, "lane", -1);
            Require(t >= 0 && t >= previous + 100 && t <= elapsed && t % 20 == 0 && next is >= 0 and <= 3 && ((next - lane + 4) % 4 is 1 or 3), "Secuencia de juego inválida.");
            moves.Add(new(t, next)); previous = t; lane = next;
        }
        Require(MoveHash(moves.Where(x => x.T <= priorElapsed)) == Str(saved, "rushPrefixHash"), "No se puede cambiar una parte de la partida ya guardada.");
        var sim = new PotSimulation(Num(saved, "rushSeed"), Num(saved, "rushBaseScore"), Num(saved, "rushBaseNegatives")); sim.Advance(elapsed, moves);
        saved["score"] = sim.Score; saved["hits"] = Num(saved, "rushBaseHits") + sim.Hits; saved["misses"] = Num(saved, "rushBaseMisses") + sim.Misses;
        saved["streak"] = sim.Streak; saved["bestStreak"] = Math.Max(Num(saved, "rushBaseBest"), sim.Best);
        saved["negativeHits"] = sim.NegativeHits; saved["penaltySeconds"] = Num(saved, "rushBasePenaltySeconds") + sim.PenaltySeconds;
        saved["rushPauseUntil"] = Math.Min(duration, sim.PauseUntil);
        saved["usedSeconds"] = Math.Min(900, Num(saved, "rushUsedBefore") + (elapsed + 999) / 1000);
        saved["rushElapsed"] = elapsed; saved["rushPrefixHash"] = MoveHash(moves); saved["rushEarned"] = sim.Score - Num(saved, "rushBaseScore");
        saved["rushCompleted"] = !checkpoint || elapsed == duration;
        saved["rushUntil"] = started.AddMilliseconds(Math.Min(duration, elapsed + 20000)).ToString("O");
        actor.GameJson = saved.ToJsonString();
        return Task.FromResult<object>(new { ok = true, state = Game(actor), earned = Num(saved, "rushEarned"), completed = Str(saved, "rushCompleted") == "true" });
    }
}
