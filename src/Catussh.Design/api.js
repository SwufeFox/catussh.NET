/*
 * api.js — the client for the .NET host's loopback API.
 *
 * The window passes ?api=<port>; the host serves saved servers, identities and live
 * monitor sessions there. Opened as a plain file (or through tools/serve.mjs) there
 * is no host, and every call resolves to `null` so the pages fall back to their
 * empty states instead of pretending there is data.
 */

const Api = (() => {
  const port = new URLSearchParams(location.search).get("api");
  const base = port && /^\d+$/.test(port) ? `http://127.0.0.1:${port}` : null;

  /** True when a .NET host is behind this page. */
  const available = base !== null;

  async function call(path, options = {}) {
    if (!base) return null;
    try {
      const response = await fetch(base + path, {
        cache: "no-store",
        headers: { "content-type": "application/json" },
        ...options,
      });
      if (!response.ok) {
        const detail = await response.text().catch(() => "");
        return { error: detail || `HTTP ${response.status}` };
      }
      return await response.json();
    } catch (error) {
      return { error: String(error) };
    }
  }

  return {
    available,
    /** Saved servers grouped for the Servers table, plus the recent list. */
    machines: () => call("/machines"),
    addMachine: (body) => call("/machines", { method: "POST", body: JSON.stringify(body) }),
    removeMachine: (id) => call(`/machines/${encodeURIComponent(id)}`, { method: "DELETE" }),

    identities: () => call("/identities"),
    addIdentity: (body) => call("/identities", { method: "POST", body: JSON.stringify(body) }),
    removeIdentity: (id) => call(`/identities/${encodeURIComponent(id)}`, { method: "DELETE" }),

    /** Starts (or reuses) a server's live monitor session. */
    startMonitor: (id) => call(`/monitor/${encodeURIComponent(id)}`, { method: "POST" }),
    /** The session's phase, last error and latest reading. */
    monitor: (id) => call(`/monitor/${encodeURIComponent(id)}`),
    /** The built-in demo reading, for the page opened without saved servers. */
    demoMonitor: () => call("/monitor"),
  };
})();
