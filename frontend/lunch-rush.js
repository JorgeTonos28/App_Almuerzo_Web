// Keep the fixed-step, integer simulation in sync with Services/LunchRush.cs.
function createPotSimulation(seed, baseScore, baseNegatives = 0) {
  let random = seed >>> 0, moveIndex = 0, nextSpawn = 600, serial = 0, lastSpecial = -10000, normalCount = 0;
  const s = { time: -20, lane: 1, score: Math.max(0, Math.min(999999, baseScore)), hits: 0, misses: 0,
    streak: 0, best: 0, frenzyUntil: 0, slowUntil: 0, shieldUntil: 0, negativeHits: Math.max(0, baseNegatives),
    pauseUntil: 0, penaltySeconds: 0, drops: [], feedback: '¡A cocinar!', event: 0 };
  // 0, 500, 1000, 1500, 2000, 2500, 3000, 4000: the final climb spans 1000 points.
  const level = () => s.score >= 4000 ? 8 : Math.min(7, Math.floor(s.score / 500) + 1);
  const fallDuration = () => 3135 - Math.floor((level() - 1) * 2275 / 7);
  const next = max => { random ^= random << 13; random ^= random >>> 17; random ^= random << 5; random >>>= 0; return random % max; };
  const say = text => { s.feedback = text; s.event++; };
  function advance(elapsed, moves) {
    for (let t = s.time + 20; t <= elapsed; t += 20) {
      s.time = t;
      while (moveIndex < moves.length && moves[moveIndex].t <= t) s.lane = moves[moveIndex++].lane;
      if (t < s.pauseUntil) continue;
      if (t >= nextSpawn) {
        let kind = 'food';
        if (t - lastSpecial >= 18000 && next(100) < 6) {
          const special = next(level() >= 3 ? 3 : 2);
          kind = special === 0 ? 'feast' : special === 1 ? 'shield' : 'slow'; lastSpecial = t;
        } else if (t >= s.frenzyUntil) {
          normalCount++; if (normalCount % 5 === 0 || next(100) < 9) kind = 'burnt';
        }
        s.drops.push({ id: serial++, lane: next(4), food: next(6), kind, progress: 0 });
        nextSpawn = t + Math.max(480, 1150 - level() * 80);
      }
      let fall = fallDuration();
      if (t < s.slowUntil) fall = Math.floor(fall * 17 / 10);
      const kept = []; let paused = false;
      for (const drop of s.drops) {
        drop.progress += Math.floor(200000 / fall);
        const caught = drop.progress >= 7600 && drop.progress <= 9000 && drop.lane === s.lane;
        if (caught) {
          if (drop.kind === 'food') {
            s.hits++; s.streak++; s.best = Math.max(s.best, s.streak);
            const points = (10 + Math.min(5, Math.floor(s.streak / 5))) * (t < s.frenzyUntil ? 3 : 1);
            s.score = Math.min(999999, s.score + points); say(`+${points} · ¡A la olla!`);
          } else if (drop.kind === 'burnt') {
            if (t >= s.shieldUntil && t >= s.frenzyUntil) {
              s.score = Math.max(0, s.score - 12); s.misses++; s.streak = 0; s.negativeHits++; say('−12 · ¡Ingrediente peligroso!');
              if (s.negativeHits % 3 === 0) {
                const penalty = s.negativeHits / 3;
                s.score = Math.max(0, s.score - 30 * penalty); s.pauseUntil = t + 3000 * penalty; s.penaltySeconds += 3 * penalty;
                nextSpawn = s.pauseUntil + 600; paused = true; say(`−${30 * penalty} extra · Cocina en pausa ${3 * penalty}s`); break;
              }
            }
          } else if (drop.kind === 'feast') { s.frenzyUntil = t + 10000; say('⭐ ¡Festín! Puntos ×3 y sin fuego'); }
          else if (drop.kind === 'shield') { s.shieldUntil = t + 10000; say('🛡️ Olla protegida'); }
          else if (drop.kind === 'slow') { s.slowUntil = t + 8000; say('🧊 Un respiro en la cocina'); }
          continue;
        }
        if (drop.progress > 9400) { if (drop.kind === 'food') { s.misses++; s.streak = 0; say('Ingrediente perdido · Racha reiniciada'); } continue; }
        kept.push(drop);
      }
      s.drops = paused ? [] : kept;
      if (t < s.frenzyUntil) s.drops = s.drops.filter(d => d.kind !== 'burnt');
    }
    return s;
  }
  function renderDrops(realElapsed, height) {
    if (realElapsed < s.pauseUntil) return [];
    let fall = fallDuration();
    if (s.time < s.slowUntil) fall = Math.floor(fall * 17 / 10);
    const fraction = Math.min(1, Math.max(0, (realElapsed - s.time) / 20));
    return s.drops.map(d => ({ ...d, y: (d.progress + fraction * Math.floor(200000 / fall)) / 10000 * height }));
  }
  return { state: s, advance, level, fallDuration, renderDrops };
}

function createLunchRush(setState) {
  const state = Vue.reactive({ active: false, busy: false, syncing: false, id: '', duration: 900, level: 1, remaining: 900,
    lane: 1, drops: [], points: 0, total: 0, streak: 0, message: '', error: '', feedback: '', pending: false, effects: [], event: 0,
    negativeHits: 0, paused: false, pauseSeconds: 0, penaltyLoss: 0, waitRemaining: 0, levelUp: 0, levelUpUntil: 0, levelUpEvent: 0 });
  const food = ['🍚', '🥦', '🍗', '🥕', '🥑', '🍅'];
  const icons = { feast: '⭐', shield: '🛡️', slow: '🧊' };
  const hazards = ['🔥', '💣', '🦠', '🪨'];
  const hazardIcon = d => hazards[((d.id + d.food) % hazards.length + hazards.length) % hazards.length];
  let animation = 0, started = 0, moves = [], simulation, baseScore = 0, nextSave = 10000, lastSaved = 0, stopping = null, inflight = null, finalPayload = null;
  let boardHeight = 330, resizeObserver = null, restTimer = 0;
  const rpc = (method, ...args) => new Promise((resolve, reject) => {
    window.almuerzo.run.withSuccessHandler(res => res.ok ? resolve(res) : reject(new Error(res.msg)))
      .withFailureHandler(reject)[method](...args);
  });
  const rawTime = () => Math.min(state.duration * 1000, performance.now() - started);
  const time = () => Math.floor(rawTime() / 20) * 20;
  const clock = value => `${Math.floor(value / 60)}:${String(value % 60).padStart(2, '0')}`;
  function move(lane) {
    if (!state.active || state.busy || state.paused) return;
    lane = ((lane % 4) + 4) % 4;
    const t = Math.max(20, time() + 20);
    if (lane === state.lane || t <= lastSaved || (moves.length && t < moves[moves.length - 1].t + 100)) return;
    if (moves.length >= 9000) { finish(); return; }
    moves.push({ t, lane }); state.lane = lane;
  }
  function paint(elapsed, realElapsed = elapsed) {
    const s = simulation.advance(elapsed, moves);
    state.remaining = Math.max(0, Math.ceil(state.duration - elapsed / 1000));
    const nextLevel = simulation.level();
    if (nextLevel > state.level) { state.levelUp = nextLevel; state.levelUpUntil = elapsed + 950; state.levelUpEvent++; }
    else if (nextLevel < state.level || elapsed >= state.levelUpUntil) state.levelUp = 0;
    state.total = s.score; state.points = s.score - baseScore; state.level = nextLevel; state.streak = s.streak;
    state.drops = simulation.renderDrops(realElapsed, boardHeight).map(d => ({ ...d, icon: d.kind === 'burnt' ? hazardIcon(d) : icons[d.kind] || food[d.food] }));
    state.negativeHits = s.negativeHits; state.paused = elapsed < s.pauseUntil;
    state.pauseSeconds = Math.max(0, Math.ceil((s.pauseUntil - elapsed) / 1000)); state.penaltyLoss = 30 * Math.floor(s.negativeHits / 3);
    state.feedback = s.feedback; state.event = s.event;
    state.effects = [['feast', '⭐ Festín ×3', s.frenzyUntil], ['shield', '🛡️ Protección', s.shieldUntil], ['slow', '🧊 Pausa fresca', s.slowUntil]]
      .filter(e => e[2] > elapsed).map(e => ({ kind: e[0], label: e[1], seconds: Math.ceil((e[2] - elapsed) / 1000) }));
  }
  function payload(elapsed) { return { id: state.id, elapsedMs: elapsed, moves: moves.filter(m => m.t <= elapsed).map(m => ({ ...m })) }; }
  function restoreWaiting(game) {
    clearInterval(restTimer); restTimer = 0;
    const remaining = Number(game.cooldownRemainingMs || 0), since = performance.now();
    let lastConsumed = -1, lastRemaining = -1;
    state.waitRemaining = Math.ceil(remaining / 1000);
    if (!remaining) { setState(game); return; }
    const update = () => {
      const elapsed = Math.min(remaining, Math.max(0, performance.now() - since));
      state.waitRemaining = Math.max(0, Math.ceil((remaining - elapsed) / 1000));
      const budget = Number(game.cooldownBudgetSeconds || 0);
      const consumed = state.waitRemaining === 0 ? budget : Math.min(budget, Math.floor(elapsed / 1000));
      if (consumed !== lastConsumed || state.waitRemaining !== lastRemaining) {
        setState({ ...game, usedSeconds: Math.min(900, game.usedSeconds + consumed), remainingSeconds: Math.max(0, game.remainingSeconds - consumed) });
        lastConsumed = consumed; lastRemaining = state.waitRemaining;
      }
      if (!state.waitRemaining) { clearInterval(restTimer); restTimer = 0; }
    };
    restTimer = setInterval(update, 100);
  }
  async function checkpoint(elapsed) {
    if (inflight || !state.active) return;
    const p = payload(elapsed); state.syncing = true;
    inflight = rpc('apiCheckpointLunchRush', p);
    try { const res = await inflight; lastSaved = elapsed; setState(res.state); if (res.completed && state.active) finish(); }
    catch (error) { if (state.active) { state.error = error.message || 'Se perdió la conexión. Reintenta guardar la partida.'; state.active = false; finalPayload = p; state.pending = true; cancelAnimationFrame(animation); } }
    finally { inflight = null; state.syncing = false; }
  }
  async function finish() {
    if (stopping) return stopping;
    if (!state.id) return;
    // Freeze once: retries use exactly the same elapsed time and controls.
    if (state.active) { const elapsed = Math.min(time(), lastSaved + 20000); paint(elapsed); finalPayload = payload(elapsed); }
    state.active = false; cancelAnimationFrame(animation); state.busy = true;
    stopping = (async () => {
      if (inflight) { try { await inflight; } catch {} }
      try {
        const res = await rpc('apiFinishLunchRush', finalPayload || payload(lastSaved));
        restoreWaiting(res.state); state.message = `Partida guardada · ${res.earned >= 0 ? '+' : ''}${res.earned} puntos`;
        state.pending = false; state.id = ''; state.error = ''; finalPayload = null;
      } catch (error) { state.error = error.message || 'No se pudo guardar. Reintenta.'; state.pending = true; }
      finally { state.busy = false; stopping = null; }
    })();
    return stopping;
  }
  function frame() {
    if (!state.active) return;
    const elapsed = time();
    if (elapsed >= lastSaved + 20000) { state.error = 'La conexión tardó demasiado. Guardando hasta el último tramo válido.'; finish(); return; }
    paint(elapsed, rawTime());
    if (elapsed >= state.duration * 1000) { finish(); return; }
    if (elapsed >= nextSave && !inflight) { nextSave = elapsed + 10000; checkpoint(elapsed); }
    animation = requestAnimationFrame(frame);
  }
  async function start() {
    if (state.busy || state.active || state.pending || state.waitRemaining > 0) return;
    state.busy = true; state.error = ''; state.message = '';
    try {
      const res = await rpc('apiStartLunchRush', 'pot-v5');
      clearInterval(restTimer); restTimer = 0; state.waitRemaining = 0;
      setState(res.state); baseScore = res.baseScore; simulation = createPotSimulation(res.seed, baseScore, res.baseNegatives);
      Object.assign(state, { id: res.id, duration: res.duration, remaining: res.duration, level: simulation.level(), points: 0,
        total: baseScore, streak: 0, lane: 1, feedback: '¡La cocina está lista!', active: true, effects: [], paused: false, negativeHits: res.baseNegatives, levelUp: 0, levelUpUntil: 0 });
      moves = []; lastSaved = 0; nextSave = 10000; finalPayload = null; started = performance.now();
      await Vue.nextTick();
      resizeObserver?.disconnect();
      const board = document.getElementById('rush-board'), lanes = board?.querySelector('.rush-lanes');
      if (lanes) { boardHeight = lanes.clientHeight; resizeObserver = new ResizeObserver(() => { boardHeight = lanes.clientHeight; }); resizeObserver.observe(lanes); }
      board?.focus(); animation = requestAnimationFrame(frame);
    } catch (error) { state.error = error.message || 'No se pudo iniciar la partida.'; }
    finally { state.busy = false; }
  }
  function key(event) {
    if (!state.active || event.altKey || event.ctrlKey || event.metaKey || /INPUT|TEXTAREA|SELECT/.test(event.target.tagName)) return;
    const k = event.key.toLowerCase();
    if (['arrowleft', 'a', 'arrowright', 'd', 'escape'].includes(k)) event.preventDefault();
    if (k === 'arrowleft' || k === 'a') move(state.lane - 1);
    if (k === 'arrowright' || k === 'd') move(state.lane + 1);
    if (k === 'escape') finish();
  }
  const hidden = () => { if (document.hidden && state.active) finish(); };
  const leaving = () => {
    if (!state.active || !state.id) return;
    const elapsed = Math.min(time(), lastSaved + 20000);
    fetch('/api/rpc', { method: 'POST', keepalive: true, credentials: 'same-origin', headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': document.querySelector('meta[name=csrf-token]').content },
      body: JSON.stringify({ method: 'apiFinishLunchRush', args: [payload(elapsed)] }) }).catch(() => {});
  };
  window.addEventListener('keydown', key); document.addEventListener('visibilitychange', hidden); window.addEventListener('pagehide', leaving);
  Vue.onMounted(() => { const game = window.__INITIAL_DATA__?.chefGame; if (game) restoreWaiting(game); });
  Vue.onUnmounted(() => { cancelAnimationFrame(animation); clearInterval(restTimer); resizeObserver?.disconnect(); window.removeEventListener('keydown', key); document.removeEventListener('visibilitychange', hidden); window.removeEventListener('pagehide', leaving); });
  return { state, start, finish, move, clock };
}
