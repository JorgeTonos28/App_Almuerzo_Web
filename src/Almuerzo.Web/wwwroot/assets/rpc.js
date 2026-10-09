// Immutable per-call handlers preserve the existing asynchronous UI contract.
window.almuerzo = {
  get run() {
    const chain = (success = null, failure = null) => new Proxy({}, {
      get(_, name) {
        if (name === 'withSuccessHandler') return fn => chain(fn, failure);
        if (name === 'withFailureHandler') return fn => chain(success, fn);
        if (typeof name !== 'string' || !/^api[A-Za-z]+$/.test(name)) return undefined;
        return async (...args) => {
          try {
            const response = await fetch('/api/rpc', { method: 'POST', credentials: 'same-origin',
              headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': document.querySelector('meta[name="csrf-token"]').content },
              body: JSON.stringify({ method: name, args }) });
            if (response.status === 401 || response.status === 403) { window.location.assign('/Auth/Denied'); return; }
            const result = await response.json();
            if (!response.ok) throw new Error(result.msg || 'No se pudo completar la operación.');
            if (success) success(result);
            else if (result?.ok === false) window.alert(result.msg);
          } catch (error) { if (failure) failure(error); else window.alert(error.message || 'Error de conexión.'); }
        };
      }
    });
    return chain();
  }
};
