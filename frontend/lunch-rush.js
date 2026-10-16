// Bandeja Express. The server reserves play time and replays these lane changes to score each round.
function createLunchRush(setState, getScore) {
  const state = Vue.reactive({ active: false, busy: false, id: '', duration: 45, level: 1, remaining: 45,
    lane: 1, drops: [], points: 0, streak: 0, message: '', error: '', feedback: '', pending: false });
  const food = ['🍚', '🥦', '🍗', '🥕', '🥑', '🍅'];
  let animation = 0, started = 0, schedule = [], moves = [], resolved = new Set(), fallMs = 2700;
  const rpc = (method, ...args) => new Promise((resolve, reject) => {
    window.almuerzo.run.withSuccessHandler(res => res.ok ? resolve(res) : reject(new Error(res.msg)))
      .withFailureHandler(reject)[method](...args);
  });
  function move(lane) {
    if (!state.active || state.busy) return;
    lane = Math.max(0, Math.min(3, lane));
    if (lane === state.lane) return;
    const t = Math.floor(performance.now() - started);
    if (moves.length && t <= moves[moves.length - 1].t || moves.length >= 450) return;
    moves.push({ t, lane }); state.lane = lane;
  }
  async function finish() {
    if (!state.id || state.busy) return;
    cancelAnimationFrame(animation); state.active = false; state.busy = true;
    try {
      const res = await rpc('apiFinishLunchRush', { id: state.id, moves });
      setState(res.state); state.message = `Partida guardada · ${res.earned >= 0 ? '+' : ''}${res.earned} puntos`;
      state.pending = false; state.id = ''; state.error = '';
    } catch (error) { state.error = error.message || 'No se pudo guardar. Reintenta.'; state.pending = true; }
    finally { state.busy = false; }
  }
  function frame() {
    if (!state.active) return;
    const elapsed = performance.now() - started;
    state.remaining = Math.max(0, Math.ceil(state.duration - elapsed / 1000));
    state.drops = schedule.filter(d => elapsed >= d.atMs - fallMs && elapsed < d.atMs + 220)
      .map(d => ({ ...d, icon: d.burnt ? '🔥' : food[d.food], y: Math.min(100, Math.max(0, (elapsed - d.atMs + fallMs) / fallMs * 86)) }));
    for (const d of schedule) {
      if (resolved.has(d.id) || elapsed < d.atMs) continue;
      resolved.add(d.id);
      if (d.lane === state.lane && !d.burnt) {
        state.streak++; const points = 10 + Math.min(5, Math.floor(state.streak / 5)); state.points += points;
        state.feedback = `+${points} ${state.streak >= 5 ? '¡Buen servicio!' : '¡A la bandeja!'}`;
      } else if (d.lane === state.lane || !d.burnt) {
        state.streak = 0; if (d.burnt) state.points -= 8;
        state.feedback = d.burnt ? '¡Cuidado con lo quemado! −8' : '¡Se escapó un plato!';
      }
    }
    if (elapsed >= state.duration * 1000) { finish(); return; }
    animation = requestAnimationFrame(frame);
  }
  async function start() {
    if (state.busy || state.active || state.pending) return;
    state.busy = true; state.error = ''; state.message = '';
    try {
      const res = await rpc('apiStartLunchRush');
      setState(res.state); Object.assign(state, { id: res.id, duration: res.duration, remaining: res.duration, level: res.level, points: 0, streak: 0, lane: 1, feedback: '¡La cocina está lista!', active: true });
      schedule = res.drops; fallMs = res.fallMs; moves = []; resolved = new Set(); started = performance.now();
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
  window.addEventListener('keydown', key); document.addEventListener('visibilitychange', hidden);
  Vue.onUnmounted(() => { cancelAnimationFrame(animation); window.removeEventListener('keydown', key); document.removeEventListener('visibilitychange', hidden); });
  return { state, start, finish, move };
}
