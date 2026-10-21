// Keep the fixed-step, integer simulation in sync with Services/LunchRush.cs.
function createPotSimulation(seed, baseScore) {
  let random = seed >>> 0, moveIndex = 0, nextSpawn = 600, serial = 0, lastSpecial = -10000, normalCount = 0;
  const s = { time: -50, lane: 1, score: Math.max(0, Math.min(999999, baseScore)), hits: 0, misses: 0,
    streak: 0, best: 0, frenzyUntil: 0, slowUntil: 0, shieldUntil: 0, drops: [], feedback: '¡A cocinar!', event: 0 };
  const level = () => Math.max(1, Math.min(8, Math.floor(s.score / 300) + 1));
  const next = max => { random ^= random << 13; random ^= random >>> 17; random ^= random << 5; random >>>= 0; return random % max; };
  const say = text => { s.feedback = text; s.event++; };
  function advance(elapsed, moves) {
    for (let t = s.time + 50; t <= elapsed; t += 50) {
      s.time = t;
      while (moveIndex < moves.length && moves[moveIndex].t <= t) s.lane = moves[moveIndex++].lane;
      if (t >= nextSpawn) {
        let kind = 'food';
        if (t - lastSpecial >= 18000 && next(100) < 6) {
          const special = next(level() >= 3 ? 3 : 2);
          kind = special === 0 ? 'feast' : special === 1 ? 'shield' : 'slow'; lastSpecial = t;
        } else if (t >= s.frenzyUntil) {
          normalCount++; if (normalCount % 5 === 0 || next(100) < 9) kind = 'burnt';
        }
        s.drops.push({ id: serial++, lane: next(4), food: next(6), kind, progress: 0 });
        nextSpawn = t + Math.max(550, 1150 - level() * 70);
      }
      let fall = Math.max(1500, 3400 - level() * 210);
      if (t < s.slowUntil) fall = Math.floor(fall * 17 / 10);
      s.drops = s.drops.filter(drop => {
        drop.progress += Math.floor(500000 / fall);
        const caught = drop.progress >= 7600 && drop.progress <= 9000 && drop.lane === s.lane;
        if (caught) {
          if (drop.kind === 'food') {
            s.hits++; s.streak++; s.best = Math.max(s.best, s.streak);
            const points = (10 + Math.min(5, Math.floor(s.streak / 5))) * (t < s.frenzyUntil ? 3 : 1);
            s.score = Math.min(999999, s.score + points); say(`+${points} · ¡A la olla!`);
          } else if (drop.kind === 'burnt') {
            if (t >= s.shieldUntil && t >= s.frenzyUntil) { s.score = Math.max(0, s.score - 12); s.misses++; s.streak = 0; say('−12 · ¡Se quemó!'); }
            else say('¡Protegido!');
          } else if (drop.kind === 'feast') { s.frenzyUntil = t + 10000; say('⭐ ¡Festín! Puntos ×3 y sin fuego'); }
          else if (drop.kind === 'shield') { s.shieldUntil = t + 10000; say('🛡️ Olla protegida'); }
          else if (drop.kind === 'slow') { s.slowUntil = t + 8000; say('🧊 Un respiro en la cocina'); }
          return false;
        }
        if (drop.progress > 9400) { if (drop.kind === 'food') { s.misses++; s.streak = 0; say('Se escapó un ingrediente'); } return false; }
        return true;
      });
      if (t < s.frenzyUntil) s.drops = s.drops.filter(d => d.kind !== 'burnt');
    }
    return s;
  }
  return { state: s, advance, level };
}

function createLunchRush(setState) {
  const state = Vue.reactive({ active: false, busy: false, syncing: false, id: '', duration: 900, level: 1, remaining: 900,
    lane: 1, drops: [], points: 0, total: 0, streak: 0, message: '', error: '', feedback: '', pending: false, effects: [], event: 0 });
  const food = ['🍚', '🥦', '🍗', '🥕', '🥑', '🍅'];
  const icons = { burnt: '🔥', feast: '⭐', shield: '🛡️', slow: '🧊' };
  let animation = 0, started = 0, moves = [], simulation, baseScore = 0, nextSave = 10000, lastSaved = 0, stopping = null, inflight = null, finalPayload = null;
  const rpc = (method, ...args) => new Promise((resolve, reject) => {
    window.almuerzo.run.withSuccessHandler(res => res.ok ? resolve(res) : reject(new Error(res.msg)))
      .withFailureHandler(reject)[method](...args);
  });
  const time = () => Math.min(state.duration * 1000, Math.floor((performance.now() - started) / 50) * 50);
  const clock = value => `${Math.floor(value / 60)}:${String(value % 60).padStart(2, '0')}`;
  function move(lane) {
    if (!state.active || state.busy) return;
    lane = ((lane % 4) + 4) % 4;
    const t = Math.max(50, time() + 50);
    if (lane === state.lane || t <= lastSaved || (moves.length && t < moves[moves.length - 1].t + 100)) return;
    if (moves.length >= 9000) { finish(); return; }
    moves.push({ t, lane }); state.lane = lane;
  }
  function paint(elapsed) {
    const s = simulation.advance(elapsed, moves);
    state.remaining = Math.max(0, Math.ceil(state.duration - elapsed / 1000));
    state.total = s.score; state.points = s.score - baseScore; state.level = simulation.level(); state.streak = s.streak;
    state.drops = s.drops.map(d => ({ ...d, icon: icons[d.kind] || food[d.food], y: d.progress / 100 }));
    state.feedback = s.feedback; state.event = s.event;
    state.effects = [['feast', '⭐ Festín ×3', s.frenzyUntil], ['shield', '🛡️ Protección', s.shieldUntil], ['slow', '🧊 Pausa fresca', s.slowUntil]]
      .filter(e => e[2] > elapsed).map(e => ({ kind: e[0], label: e[1], seconds: Math.ceil((e[2] - elapsed) / 1000) }));
  }
  function payload(elapsed) { return { id: state.id, elapsedMs: elapsed, moves: moves.filter(m => m.t <= elapsed).map(m => ({ ...m })) }; }
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
        setState(res.state); state.message = `Partida guardada · ${res.earned >= 0 ? '+' : ''}${res.earned} puntos`;
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
    paint(elapsed);
    if (elapsed >= state.duration * 1000) { finish(); return; }
    if (elapsed >= nextSave && !inflight) { nextSave = elapsed + 10000; checkpoint(elapsed); }
    animation = requestAnimationFrame(frame);
  }
  async function start() {
    if (state.busy || state.active || state.pending) return;
    state.busy = true; state.error = ''; state.message = '';
    try {
      const res = await rpc('apiStartLunchRush');
      setState(res.state); baseScore = res.baseScore; simulation = createPotSimulation(res.seed, baseScore);
      Object.assign(state, { id: res.id, duration: res.duration, remaining: res.duration, level: simulation.level(), points: 0,
        total: baseScore, streak: 0, lane: 1, feedback: '¡La cocina está lista!', active: true, effects: [] });
      moves = []; lastSaved = 0; nextSave = 10000; finalPayload = null; started = performance.now();
      await Vue.nextTick(); document.getElementById('rush-board')?.focus(); animation = requestAnimationFrame(frame);
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
  Vue.onUnmounted(() => { cancelAnimationFrame(animation); window.removeEventListener('keydown', key); document.removeEventListener('visibilitychange', hidden); window.removeEventListener('pagehide', leaving); });
  return { state, start, finish, move, clock };
}
