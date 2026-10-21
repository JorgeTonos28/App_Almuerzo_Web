(() => {
  const form = document.getElementById('login-form');
  if (!form) return;
  const button = form.querySelector('button[type="submit"]');
  const error = document.getElementById('login-error');
  const password = form.querySelector('[name="Password"]');
  let busy = false;
  form.addEventListener('submit', async event => {
    event.preventDefault();
    if (busy) return;
    busy = true; button.disabled = true; button.textContent = 'Validando…'; form.setAttribute('aria-busy', 'true');
    error.hidden = true;
    form.querySelector('[data-valmsg-summary]')?.replaceChildren();
    try {
      const response = await fetch(form.action, { method: 'POST', body: new FormData(form), credentials: 'same-origin', headers: { Accept: 'application/json' }, redirect: 'error' });
      if (!response.ok) throw new Error(response.status === 429 ? 'Demasiados intentos. Espera unos minutos antes de volver a ingresar.' : 'No se pudo validar el acceso. Recarga la página e intenta nuevamente.');
      const result = await response.json();
      if (!result.ok) throw new Error(result.msg || 'No se pudo iniciar sesión.');
      const destination = new URL(result.redirectUrl, window.location.origin);
      if (destination.origin !== window.location.origin) throw new Error('La respuesta de acceso no es válida.');
      window.location.replace(destination.href);
    } catch (exception) {
      error.textContent = exception.message || 'No se pudo conectar. Intenta nuevamente.';
      error.hidden = false; password.value = ''; password.focus();
      busy = false; button.disabled = false; button.textContent = 'Ingresar'; form.removeAttribute('aria-busy');
    }
  });
})();
