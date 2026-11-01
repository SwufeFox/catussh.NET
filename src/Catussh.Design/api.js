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
        const detail = await response.json().catch(() => null);
        return {
          error: detail?.error ?? `HTTP ${response.status}`,
          code: detail?.code,
          tokenExpired: detail?.tokenExpired ?? false,
        };
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

    folders: () => call("/folders"),
    createFolder: (name) => call("/folders", { method: "POST", body: JSON.stringify({ name }) }),
    renameFolder: (name, newName) => call(`/folders/${encodeURIComponent(name)}`, { method: "POST", body: JSON.stringify({ newName }) }),
    removeFolder: (name) => call(`/folders/${encodeURIComponent(name)}`, { method: "DELETE" }),
    moveMachine: (id, name) => call(`/machines/${encodeURIComponent(id)}/folder`, { method: "POST", body: JSON.stringify({ name }) }),

    identities: () => call("/identities"),
    addIdentity: (body) => call("/identities", { method: "POST", body: JSON.stringify(body) }),
    removeIdentity: (id) => call(`/identities/${encodeURIComponent(id)}`, { method: "DELETE" }),

    /** Starts (or reuses) a server's live monitor session. */
    startMonitor: (id) => call(`/monitor/${encodeURIComponent(id)}`, { method: "POST" }),
    /** The session's phase, last error and latest reading. */
    monitor: (id) => call(`/monitor/${encodeURIComponent(id)}`),
    /** The built-in demo reading, for the page opened without saved servers. */
    demoMonitor: () => call("/monitor"),
    startDesktop: (id, purpose = "desktop") => call(`/desktop/${encodeURIComponent(id)}`, { method: "POST", body: JSON.stringify({ purpose }) }),
    desktop: (id, after = 0) => call(`/desktop/${encodeURIComponent(id)}?after=${after}`),
    stopDesktop: (id) => call(`/desktop/${encodeURIComponent(id)}`, { method: "DELETE" }),
    desktopPointer: (id, body) => call(`/desktop/${encodeURIComponent(id)}/pointer`, { method: "POST", body: JSON.stringify(body) }),
    desktopKey: (id, body) => call(`/desktop/${encodeURIComponent(id)}/key`, { method: "POST", body: JSON.stringify(body) }),
    desktopSignal: (id, body) => call(`/desktop/${encodeURIComponent(id)}/signal`, { method: "POST", body: JSON.stringify(body) }),
    startTerminal: (id) => call(`/terminal/${encodeURIComponent(id)}`, { method: "POST" }),
    terminal: (id) => call(`/terminal/${encodeURIComponent(id)}`),
    sendTerminal: (id, data) => call(`/terminal/${encodeURIComponent(id)}/input`, { method: "POST", body: JSON.stringify({ data }) }),
    resizeTerminal: (id, columns, rows) => call(`/terminal/${encodeURIComponent(id)}/resize`, { method: "POST", body: JSON.stringify({ columns, rows }) }),
    stopTerminal: (id) => call(`/terminal/${encodeURIComponent(id)}`, { method: "DELETE" }),
    listFiles: (id, path) => call(`/files/${encodeURIComponent(id)}?path=${encodeURIComponent(path)}`),
    createDirectory: (id, path) => call(`/files/${encodeURIComponent(id)}/directory`, { method: "POST", body: JSON.stringify({ path }) }),
    uploadFile: (id, path, file, overwrite = true) => call(`/files/${encodeURIComponent(id)}/upload?path=${encodeURIComponent(path)}&overwrite=${overwrite}`, { method: "POST", headers: { "content-type": "application/octet-stream" }, body: file }),
    downloadFile: async (id, path) => {
      if (!base) return null;
      try {
        const response = await fetch(`${base}/files/${encodeURIComponent(id)}/download?path=${encodeURIComponent(path)}`, { cache: "no-store" });
        if (!response.ok) return await response.json().catch(() => ({ error: `HTTP ${response.status}` }));
        return { blob: await response.blob(), disposition: response.headers.get("content-disposition") };
      } catch (error) { return { error: String(error) }; }
    },
    vncDiscovery: () => call("/vnc/discovery"),
    refreshVncDiscovery: () => call("/vnc/discovery/refresh", { method: "POST" }),
    startUuQr: () => call("/uu/account/qr", { method: "POST" }),
    pollUuQr: () => call("/uu/account/qr/status", { method: "POST" }),
    sendUuSms: (body) => call("/uu/account/sms/code", { method: "POST", body: JSON.stringify(body) }),
    loginUuSms: (body) => call("/uu/account/sms/login", { method: "POST", body: JSON.stringify(body) }),
    importUuToken: (body) => call("/uu/account/token", { method: "POST", body: JSON.stringify(body) }),
    refreshUuDevices: () => call("/uu/account/devices/refresh", { method: "POST" }),
    logoutUu: () => call("/uu/account/logout", { method: "POST" }),
  };
})();
