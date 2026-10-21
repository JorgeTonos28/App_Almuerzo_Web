import fs from 'node:fs/promises';
import vm from 'node:vm';
import assert from 'node:assert/strict';

const context = vm.createContext({});
vm.runInContext(await fs.readFile('frontend/lunch-rush.js', 'utf8'), context);
const create = context.createPotSimulation;
const moves = Array.from({ length: 800 }, (_, i) => ({ t: 100 + i * 250, lane: (i + 2) % 4 }));
const vectors = [];
for (const [seed, base] of [[1, 0], [123456, 900], [2147483646, 299]]) {
  const sim = create(seed, base); sim.advance(200000, moves);
  const s = sim.state;
  vectors.push([seed, base, s.score, s.hits, s.misses, s.best, s.frenzyUntil, s.slowUntil, s.shieldUntil]);
}
// These vectors are also checked by the C# simulator tests.
console.log(JSON.stringify(vectors));
assert.deepEqual(vectors, [[1,0,960,92,76,10,185100,0,207350],[123456,900,1714,96,111,6,54800,0,84850],[2147483646,299,1213,99,78,11,175950,145900,0]]);
const touch = create(1, 299);
touch.state.drops.push({ id: 99, lane: 1, food: 0, kind: 'food', progress: 7550 });
touch.advance(0, []);
assert.equal(touch.state.hits, 1, 'Catch on first contact with the rim');
assert.equal(touch.level(), 2, 'Difficulty changes immediately when crossing a score threshold');
touch.state.drops.push({ id: 100, lane: 1, food: 0, kind: 'shield', progress: 7550 });
touch.advance(50, []);
const before = touch.state.score;
touch.state.drops.push({ id: 101, lane: 1, food: 0, kind: 'burnt', progress: 7550 });
touch.advance(100, []); assert.equal(touch.state.score, before, 'Shield ignores burnt food');
touch.state.drops.push({ id: 102, lane: 1, food: 0, kind: 'feast', progress: 7550 });
touch.advance(150, []);
touch.state.drops.push({ id: 103, lane: 2, food: 0, kind: 'burnt', progress: 5000 });
touch.state.drops.push({ id: 104, lane: 1, food: 0, kind: 'food', progress: 7550 });
touch.advance(200, []);
assert.equal(touch.state.score, before + 30); assert.equal(touch.state.drops.some(d => d.kind === 'burnt'), false);
// Spawn statistics before any contact: hazards occur regularly, specials have an 18 s cooldown.
let bad = 0, normal = 0, specials = 0;
for (let seed = 1; seed <= 100; seed++) {
  const sim = create(seed, 900), seen = new Set(); let lastSpecial = -18000;
  for (let t = 0; t <= 120000; t += 50) {
    sim.advance(t, []);
    for (const d of sim.state.drops) if (!seen.has(d.id)) {
      seen.add(d.id);
      if (d.kind === 'burnt') bad++;
      else if (d.kind === 'food') normal++;
      else { specials++; assert.ok(t - lastSpecial >= 18000); lastSpecial = t; }
    }
  }
}
assert.ok(bad / (bad + normal) > .18 && bad / (bad + normal) < .35);
assert.ok(specials / (bad + normal + specials) < .06);
console.log(`Mecánicas verificadas: fuego ${(bad / (bad + normal) * 100).toFixed(1)}%, especiales ${(specials / (bad + normal + specials) * 100).toFixed(1)}%.`);
