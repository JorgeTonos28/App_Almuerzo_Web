import fs from 'node:fs/promises';
import vm from 'node:vm';
import assert from 'node:assert/strict';

const context = vm.createContext({});
vm.runInContext(await fs.readFile('frontend/lunch-rush.js', 'utf8'), context);
const create = context.createPotSimulation;
const moves = Array.from({ length: 800 }, (_, i) => ({ t: 100 + i * 240, lane: (i + 2) % 4 }));
const vectors = [];
for (const [seed, base] of [[1, 0], [123456, 900], [2147483646, 299], [42, 3999], [98765, 6000]]) {
  const sim = create(seed, base); sim.advance(200000, moves);
  const s = sim.state;
  vectors.push([seed, base, s.score, s.hits, s.misses, s.best, s.frenzyUntil, s.slowUntil, s.shieldUntil]);
}
// These vectors are also checked by the C# simulator tests.
console.log(JSON.stringify(vectors));
assert.deepEqual(vectors, [[1,0,341,63,56,7,191300,0,0],[123456,900,737,62,55,6,35780,0,73700],[2147483646,299,752,68,57,8,194900,0,93140],[42,3999,3111,48,97,4,0,122160,0],[98765,6000,6330,75,148,5,158180,0,46740]]);
const thresholds = [[0,1,3135],[499,1,3135],[500,2,2810],[999,2,2810],[1000,3,2485],[1499,3,2485],[1500,4,2160],[1999,4,2160],[2000,5,1835],[2499,5,1835],[2500,6,1510],[2999,6,1510],[3000,7,1185],[3500,7,1185],[3999,7,1185],[4000,8,860],[999999,8,860]];
for (const [score, expectedLevel, expectedFall] of thresholds) {
  const sim = create(1, score); assert.equal(sim.level(), expectedLevel); assert.equal(sim.fallDuration(), expectedFall);
}
const finalLevel = create(1,3999);
finalLevel.state.drops.push({ id:999,lane:1,food:0,kind:'food',progress:7550 }); finalLevel.advance(0,[]);
assert.equal(finalLevel.level(),8); assert.equal(finalLevel.fallDuration(),860);
finalLevel.state.drops.push({ id:1000,lane:1,food:0,kind:'burnt',progress:7550 }); finalLevel.advance(20,[]);
assert.equal(finalLevel.level(),7);
const touch = create(1, 499);
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
  for (let t = 0; t <= 120000; t += 20) {
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
const protection = create(1, 300, 2);
const inject = (sim, kind, lane = 1) => sim.state.drops.push({ id: 9000, lane, food: 0, kind, progress: 7550 });
inject(protection, 'food'); protection.advance(0, []);
inject(protection, 'shield'); protection.advance(20, []);
for (const t of [40,60,80]) { inject(protection,'burnt'); protection.advance(t, []); }
assert.equal(protection.state.streak, 1); assert.equal(protection.state.score,310);
assert.equal(protection.state.negativeHits, 2); assert.equal(protection.state.pauseUntil,0); assert.equal(protection.state.misses,0);
const penalties = create(1, 1000);
for (const [t, kind] of [[0,'burnt'],[20,'food'],[40,'burnt'],[60,'food'],[80,'burnt']]) { inject(penalties,kind); penalties.advance(t, []); }
assert.equal(penalties.state.negativeHits,3); assert.equal(penalties.state.pauseUntil,3080); assert.equal(penalties.state.score,954);
assert.equal(penalties.state.drops.length,0);
penalties.advance(3060, []); assert.equal(penalties.state.score,954); assert.equal(penalties.state.drops.length,0);
penalties.advance(3080, []);
for (const t of [3100,3120,3140]) { inject(penalties,'burnt'); penalties.advance(t, []); }
assert.equal(penalties.state.score,858); assert.equal(penalties.state.pauseUntil,9140); assert.equal(penalties.state.penaltySeconds,9);
const smooth = create(1,0); smooth.advance(620, []);
const progress = smooth.state.drops[0].progress;
assert.ok(smooth.renderDrops(625,330)[0].y > smooth.renderDrops(620,330)[0].y);
assert.ok(smooth.renderDrops(630,330)[0].y > smooth.renderDrops(625,330)[0].y);
assert.equal(smooth.state.drops[0].progress,progress,'Rendering must not advance authoritative physics');
console.log('Protección, penalizaciones acumulativas y movimiento entre pasos verificados.');

// Exercise the actual browser controller against a local clock and an authenticated-start response.
async function controller(baseScore) {
  let elapsed = 0, frame = null, success;
  const run = { withSuccessHandler(fn) { success = fn; return this; }, withFailureHandler() { return this; },
    apiStartLunchRush(version) { assert.equal(version,'pot-v5'); success({ok:true,id:'test',seed:1,baseScore,baseNegatives:0,duration:900,state:{}}); } };
  const sandbox = vm.createContext({ Vue:{reactive:x=>x,nextTick:async()=>{},onMounted(){},onUnmounted(){}},
    window:{almuerzo:{run},addEventListener(){},removeEventListener(){}}, document:{addEventListener(){},removeEventListener(){},getElementById(){return null;}},
    performance:{now:()=>elapsed},requestAnimationFrame:fn=>{frame=fn;return 1;},cancelAnimationFrame(){},clearInterval(){},setInterval(){return 1;} });
  vm.runInContext(await fs.readFile('frontend/lunch-rush.js','utf8'),sandbox);
  const game = sandbox.createLunchRush(()=>{}); await game.start();
  return {game,tick:t=>{elapsed=t;frame();}};
}
const notice = await controller(499); assert.equal(notice.game.state.levelUp,0,'Starting a session does not announce a level gain');
let promotedAt = 0;
for (let t=0;t<=6000;t+=20) { notice.tick(t); if (notice.game.state.levelUp) { promotedAt=t; break; } }
assert.ok(promotedAt>0); assert.equal(notice.game.state.level,2); assert.equal(notice.game.state.levelUp,2);
assert.equal(notice.game.state.levelUpEvent,1); notice.tick(promotedAt+900); assert.equal(notice.game.state.levelUp,2);
notice.tick(promotedAt+960); assert.equal(notice.game.state.levelUp,0,'Level text disappears in less than a second');
const veteran = await controller(4000); veteran.tick(0); assert.equal(veteran.game.state.level,8); assert.equal(veteran.game.state.levelUp,0);
console.log('Aviso de nivel verificado: solo al subir, sin aviso falso al iniciar, duración inferior a un segundo.');
