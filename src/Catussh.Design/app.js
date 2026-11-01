/*
 * app.js — the catussh shell: sidebar, toolbar, and the four pages that the
 * original screenshots cover (Home, Servers, Identities, Snippets, Port Forward,
 * Settings).
 *
 * Ported from Application/catussh/Interface/{Shell,SidebarView,HomeView,
 * ServersView}.swift. Data is the same demo set the screenshots show so a
 * side-by-side comparison is meaningful.
 */

const icons = {
  home: `<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linecap="round" stroke-linejoin="round"><path d="M2.5 7 8 2.5 13.5 7"/><path d="M4 6.2V13h8V6.2"/><path d="M6.5 13V9.5h3V13"/></svg>`,
  servers: `<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linecap="round"><rect x="2" y="3" width="12" height="4.2" rx="1.2"/><rect x="2" y="8.8" width="12" height="4.2" rx="1.2"/><path d="M4.4 5.1h.01M4.4 10.9h.01"/></svg>`,
  identities: `<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linecap="round"><circle cx="6" cy="5.5" r="2.4"/><path d="M2.2 13c.5-2.2 2-3.4 3.8-3.4S9.3 10.8 9.8 13"/><circle cx="12" cy="9.6" r="1.2"/><path d="M13.2 9.6v2.2"/></svg>`,
  snippets: `<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linecap="round" stroke-linejoin="round"><path d="M5.6 4.6 2.8 8l2.8 3.4M10.4 4.6 13.2 8l-2.8 3.4"/></svg>`,
  portforward: `<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linecap="round" stroke-linejoin="round"><path d="M3 6.2h9.2M9.4 3.4l2.8 2.8-2.8 2.8"/><path d="M13 9.8H3.8M6.6 12.6 3.8 9.8l2.8-2.8"/></svg>`,
  settings: `<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linecap="round"><circle cx="8" cy="8" r="2.1"/><path d="M8 1.6v1.7M8 12.7v1.7M14.4 8h-1.7M3.3 8H1.6M12.5 3.5l-1.2 1.2M4.7 11.3l-1.2 1.2M12.5 12.5l-1.2-1.2M4.7 4.7 3.5 3.5"/></svg>`,
  plus: `<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round"><path d="M8 3.2v9.6M3.2 8h9.6"/></svg>`,
  terminal: `<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linecap="round" stroke-linejoin="round"><rect x="2" y="3" width="12" height="10" rx="2"/><path d="M5 6.6l1.9 1.6L5 9.8"/><path d="M8.4 10h2.6"/></svg>`,
  files: `<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linejoin="round"><path d="M2.4 5.2a1.2 1.2 0 0 1 1.2-1.2h2.6l1.2 1.3h4.8a1.2 1.2 0 0 1 1.2 1.2v4.9a1.2 1.2 0 0 1-1.2 1.2H3.6a1.2 1.2 0 0 1-1.2-1.2z"/></svg>`,
  monitor: `<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linecap="round"><circle cx="8" cy="8" r="5.6"/><path d="M8 8l2.6-1.6"/><path d="M8 2.4V4M8 12v1.6M14 8h-1.6M3.6 8H2"/></svg>`,
  more: `<svg viewBox="0 0 16 16" fill="currentColor"><circle cx="3.6" cy="8" r="1.2"/><circle cx="8" cy="8" r="1.2"/><circle cx="12.4" cy="8" r="1.2"/></svg>`,
  search: `<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"><circle cx="7.2" cy="7.2" r="4.2"/><path d="M10.6 10.6 13.4 13.4"/></svg>`,
  eye: `<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.4"><path d="M1.8 8s2.4-4 6.2-4 6.2 4 6.2 4-2.4 4-6.2 4S1.8 8 1.8 8z"/><circle cx="8" cy="8" r="1.8"/></svg>`,
  windows: `<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linejoin="round"><rect x="2.4" y="2.4" width="8" height="8" rx="1.4"/><path d="M5.6 13.6h6.6a1.4 1.4 0 0 0 1.4-1.4V5.6"/></svg>`,
};

const servers = [
  { name: "build-runner", address: "ci.example.com", group: "CI", note: "GitHub Actions runner", identity: "Automatic", lastUsed: "2 minutes ago", cpu: "—" },
  { name: "homelab-pi", address: "192.0.2.50", group: "Home Lab", note: "Raspberry Pi 5", identity: "Automatic", lastUsed: "1 minute ago", cpu: "—" },
  { name: "db-primary", address: "198.51.100.20", group: "Production", note: "PostgreSQL 16", identity: "Automatic", lastUsed: "2 minutes ago", cpu: "—" },
  { name: "web-01", address: "203.0.113.10", group: "Production", note: "nginx · edge", identity: "Automatic", lastUsed: "2 minutes ago", cpu: "—" },
  { name: "web-02", address: "203.0.113.11", group: "Production", note: "nginx · edge", identity: "Automatic", lastUsed: "2 minutes ago", cpu: "—" },
];

const homeServers = [
  { name: "web-01", address: "203.0.113.10", lastUsed: "2 minutes ago" },
  { name: "web-02", address: "203.0.113.11", lastUsed: "1 minute ago" },
  { name: "db-primary", address: "198.51.100.20", lastUsed: "1 minute ago" },
  { name: "build-runner", address: "ci.example.com", lastUsed: "1 minute ago" },
  { name: "homelab-pi", address: "192.0.2.50", lastUsed: "1 minute ago" },
];

const identities = [
  { name: "ignored", detail: "Automatic · password", badge: "1 server" },
];

const snippets = [
  { name: "Update all packages", command: "sudo apt update && sudo apt upgrade -y", badge: "Linux" },
  { name: "Disk usage", command: "df -Pk | sort -k5 -n", badge: "Run on 5 servers" },
  { name: "Restart nginx", command: "sudo systemctl restart nginx", badge: "Run on 2 servers" },
];

const portForwards = [
  { label: "Postgres", local: "localhost:5432", remote: "db-primary:5432", state: "off" },
  { label: "Grafana", local: "localhost:3000", remote: "homelab-pi:3000", state: "running" },
];

const pages = [
  { id: "home", label: "Home", icon: "home" },
  { id: "servers", label: "Servers", icon: "servers", count: () => allMachines().length },
  { id: "identities", label: "Identities", icon: "identities", count: () => (state.identities ?? []).length },
  { id: "snippets", label: "Snippets", icon: "snippets" },
  { id: "portforward", label: "Port Forward", icon: "portforward" },
];

const state = {
  page: "home",
  dark: false,

  // Saved state from the host: null means "not loaded yet", not "empty". The
  // distinction matters — an empty list should say "no servers yet", not blink.
  machines: null,
  machinesError: null,
  folders: null,
  folderInfo: null,
  identities: null,
  identitiesError: null,
  uuRemote: null,
  uuMode: "qr",
  uuCountryCode: "+86",
  uuMobile: "",
  uuSmsCode: "",
  uuTokenDraft: "",
  uuTokenUserID: "",
  uuTokenDeviceID: "",
  uuTokenClientID: "",
  uuTokenSystemID: "",
  uuQrImage: null,
  uuBusy: false,
  uuError: null,
  uuNotice: null,

  // Monitor page: the session's phase, its last error, the latest reading and
  // the two toggles the Swift cards keep in @State.
  monitor: null,
  monitorError: null,
  monitorPhase: null,
  monitorCores: false,
  monitorSwap: false,
  monitorMachineId: null,
  desktopMachineId: null,
  desktopStartedId: null,
  desktopPhase: null,
  desktopError: null,
  desktopFrameVersion: 0,
  desktopFrame: null,
  vncDiscovery: null,
  vncDiscoveryError: null,
  vncDiscoveryBusy: false,
  vncDiscoveryLoaded: false,
  desktopSignalVersion: 0,
  terminalMachineId: null,
  terminalStartedId: null,
  terminalPhase: null,
  terminalError: null,
  terminalOutput: "",
  terminalPollBusy: false,
  terminalTransport: null,
  fileMachineId: null,
  filePath: "",
  fileEntries: [],
  fileError: null,
  fileBusy: false,
  fileLoaded: false,

  // A pending dialog: "add-server" | "add-identity" | null.
  sheet: null,
  sheetError: null,
  movingMachineId: null,
  vncDraft: null,
  quickConnectValue: "",
};

/* ---------------------------------------------------------------- shell */

function sidebar() {
  return `
    <aside class="rx-sidebar">
      <div class="rx-sidebar__traffic">
        <span class="rx-traffic-light rx-traffic-light--close"></span>
        <span class="rx-traffic-light rx-traffic-light--min"></span>
        <span class="rx-traffic-light rx-traffic-light--max"></span>
      </div>
      ${pages
        .map((p, index) => {
          const selected = p.id === state.page;
          const separator = index === 1 ? `<div class="rx-sidebar__section">Manage</div>` : "";
          // Counts come from the loaded state, not from a literal: the sidebar is
          // how a person checks that adding a server actually saved it.
          const count = typeof p.count === "function" ? p.count() : p.count;
          return `${separator}
          <a class="rx-sidebar__row${selected ? " rx-sidebar__row--selected" : ""}"
             href="#${p.id}" data-nav="${p.id}">
            ${icons[p.icon]}
            <span>${p.label}</span>
            ${count ? `<span class="rx-sidebar__count">${count}</span>` : ""}
          </a>`;
        })
        .join("")}
      <div class="rx-sidebar__footer">
        <a class="rx-sidebar__row" href="#settings" data-nav="settings">
          ${icons.settings}<span>Settings</span>
        </a>
      </div>
    </aside>`;
}

function toolbar(leading, trailing) {
  return `<header class="rx-toolbar">${leading || ""}<span class="rx-spacer"></span>${trailing || ""}</header>`;
}

const primaryButton = (label, icon = icons.plus, small = false, attributes = "") =>
  `<button class="rx-button rx-button--primary${small ? " rx-button--small" : ""}" ${attributes}>${icon}${label}</button>`;

const borderedButton = (label, icon = "", small = false, attributes = "") =>
  `<button class="rx-button rx-button--bordered${small ? " rx-button--small" : ""}" ${attributes}>${icon}${label}</button>`;

const plainButton = (label, icon = "", attributes = "") =>
  `<button class="rx-button rx-button--plain" ${attributes}>${icon}${label}</button>`;

const iconButton = (icon, attributes = "", label = "More") =>
  `<button class="rx-icon-button" aria-label="${label}" ${attributes}>${icon}</button>`;

/* ---------------------------------------------------------------- pages */

/**
 * Relative time for the "last used" column: the Swift used
 * RelativeDateTimeFormatter with .full / .named, so this follows the same shape.
 */
function relativeTime(iso) {
  if (!iso) return "Never";
  const when = new Date(iso);
  if (Number.isNaN(when.getTime())) return "Never";
  if (when.getFullYear() < 1980) return "Never";

  const seconds = Math.round((Date.now() - when.getTime()) / 1000);
  if (seconds < 60) return "Now";
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) return minutes + (minutes === 1 ? " minute ago" : " minutes ago");
  const hours = Math.round(minutes / 60);
  if (hours < 24) return hours + (hours === 1 ? " hour ago" : " hours ago");
  const days = Math.round(hours / 24);
  if (days === 1) return "Yesterday";
  if (days < 30) return days + " days ago";
  const months = Math.round(days / 30);
  if (months < 12) return months + (months === 1 ? " month ago" : " months ago");
  const years = Math.round(months / 12);
  return years + (years === 1 ? " year ago" : " years ago");
}

/** The demo servers the first run starts with, used for the fixture-only preview. */
function demoMachines() {
  return [
    { id: "demo-1", name: "web-01", address: "203.0.113.10", port: 22, group: "Production", comment: "nginx · edge", identity: "Automatic", lastConnection: new Date(Date.now() - 120000).toISOString() },
    { id: "demo-2", name: "web-02", address: "203.0.113.11", port: 22, group: "Production", comment: "nginx · edge", identity: "Automatic", lastConnection: new Date(Date.now() - 60000).toISOString() },
    { id: "demo-3", name: "db-primary", address: "198.51.100.20", port: 22, group: "Production", comment: "PostgreSQL 16", identity: "Automatic", lastConnection: new Date(Date.now() - 120000).toISOString() },
    { id: "demo-4", name: "build-runner", address: "ci.example.com", port: 22, group: "CI", comment: "GitHub Actions runner", identity: "Automatic", lastConnection: new Date(Date.now() - 60000).toISOString() },
    { id: "demo-5", name: "homelab-pi", address: "192.0.2.50", port: 22, group: "Home Lab", comment: "Raspberry Pi 5", identity: "Automatic", lastConnection: new Date(Date.now() - 60000).toISOString() },
  ];
}

/** Every saved server, flattened, with the fixture standing in for a host-less preview. */
function allMachines() {
  if (state.machines === null) return demoMachines();
  const flat = [];
  for (const group of state.machines) {
    for (const item of group.items) flat.push({ ...item, group: group.group });
  }
  return flat;
}

function machinesByGroup() {
  const flat = allMachines();
  const groups = new Map();
  for (const machine of flat) {
    const key = machine.group || "Ungrouped";
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key).push(machine);
  }
  for (const folder of state.folders ?? []) {
    if (!groups.has(folder)) groups.set(folder, []);
  }
  return [...groups.entries()]
    .sort((a, b) => a[0].localeCompare(b[0]))
    .map(([group, items]) => {
      const folder = state.folderInfo?.find(item => item.name === group);
      return { group, items, linked: folder?.linked === true, source: folder?.source ?? "local" };
    });
}

/* ---------------------------------------------------------------- pages */

function homePage() {
  const recent = [...allMachines()]
    .sort((a, b) => new Date(b.lastConnection || 0) - new Date(a.lastConnection || 0))
    .slice(0, 5);

  const cards = recent
    .map(
      (s) => `
      <div class="rx-card rx-server-card">
        <div>
          <div class="rx-server-card__name">${escapeHtml(s.name)}</div>
          <div class="rx-server-card__address">${escapeHtml(s.address)}</div>
        </div>
        <div class="rx-server-card__meta">
          <div class="rx-server-card__meta-row">
            <span class="rx-caps-label">Last used</span>
            <span class="rx-spacer"></span>
            <span class="rx-server-card__meta-value">${relativeTime(s.lastConnection)}</span>
          </div>
          <div class="rx-server-card__meta-row">
            <span class="rx-caps-label">Identity</span>
            <span class="rx-spacer"></span>
            <span class="rx-server-card__meta-value">${escapeHtml(s.identity || "Automatic")}</span>
          </div>
        </div>
        <div class="rx-server-card__actions">
          ${primaryButton("Connect", icons.terminal, true, `data-connect="${s.id}"`)}
          ${s.transport === "uu-remote" ? "" : borderedButton("Files", icons.files, true, `data-files="${s.id}"`)}
          ${borderedButton("Monitor", icons.monitor, true, `data-monitor="${s.id}"`)}
          ${iconButton(icons.more, `data-remove="${s.id}"`, "Remove server")}
        </div>
      </div>`
    )
    .join("");

  const empty = recent.length === 0;

  return `
    ${toolbar(`<h1 class="rx-page-title" style="font-size:17px">Home</h1>`, primaryButton("New Server", icons.plus, false, "data-new-server"))}
    <div class="rx-page" style="padding-top: 0;">
      <form class="rx-home-search" data-quick-connect>
        ${icons.terminal}
        <input name="target" placeholder="user@host:22" spellcheck="false" autocomplete="off"
               value="${escapeHtml(state.quickConnectValue)}" />
        <button class="rx-icon-button" type="submit" aria-label="Connect">${icons.plus}</button>
      </form>
      ${state.quickConnectError ? `<div class="rx-help-text rx-help-text--error" style="text-align:center">${escapeHtml(state.quickConnectError)}</div>` : ""}
      <div class="rx-section-title-row">
        <span class="rx-section-title">Servers</span>
        <span class="rx-hint-text">${recent.length}</span>
        <span class="rx-spacer"></span>
        ${recent.length > 0 ? plainButton("Show All", "", "data-show-all") : ""}
      </div>
      ${empty
        ? `<div class="rx-card rx-empty-state">
             <span class="rx-section-title">No servers yet</span>
             <span class="rx-help-text">Type user@host above, or add one with New Server.</span>
           </div>`
        : `<div class="rx-server-grid">
             ${cards}
             <button class="rx-new-card" data-new-server>${icons.plus}<span>New Server</span></button>
           </div>`}
    </div>`;
}

function nearbyVncCard() {
  const hosts = state.vncDiscovery?.hosts ?? [];
  const rows = hosts.map(host => {
    const saved = allMachines().find(machine => machine.id === host.machineId)
      ?? allMachines().find(machine => machine.remoteDesktopProtocol === "vnc"
        && machine.address?.replace(/\.+$/, "").toLowerCase() === host.hostName?.replace(/\.+$/, "").toLowerCase()
        && Number(machine.vncPort || 5900) === Number(host.port));
    const savedId = host.machineId || saved?.id;
    return `<div style="display:flex;align-items:center;gap:var(--space-3);padding:8px 12px">
      <span class="rx-symbol-tile">${icons.windows}</span>
      <div style="min-width:0;flex:1">
        <div class="rx-body">${escapeHtml(host.displayName || host.name)}</div>
        <div class="rx-help-text rx-table__mono">${escapeHtml(host.hostName)}:${escapeHtml(host.port)}</div>
      </div>
      ${savedId
        ? `<span class="rx-help-text">${saved ? `Saved as ${escapeHtml(saved.name)}` : "Saved"}</span>${borderedButton("Desktop", "", true, `data-desktop="${escapeHtml(savedId)}"`)}`
        : `<button class="rx-button rx-button--plain" type="button" data-vnc-add="${escapeHtml(host.id)}">Add…</button>`}
    </div>`;
  }).join("");
  const message = !Api.available
    ? "Open the catussh desktop app to discover local VNC hosts."
    : state.vncDiscoveryError
      ? `<span class="rx-help-text rx-help-text--error">${escapeHtml(state.vncDiscoveryError)}</span>`
      : hosts.length
        ? rows
        : `<span class="rx-help-text">${state.vncDiscoveryBusy || !state.vncDiscoveryLoaded
          ? "Looking for Screen Sharing and VNC hosts on this network…"
          : "None found. Enable Screen Sharing or VNC on a nearby host."}</span>`;
  return `<section class="rx-card" data-vnc-nearby style="padding:var(--space-2);margin-bottom:var(--space-3)">
    <div class="rx-section-title-row" style="padding:4px 12px">
      <span class="rx-section-title">Nearby</span><span class="rx-spacer"></span>
      <button class="rx-button rx-button--plain" type="button" data-vnc-refresh ${state.vncDiscoveryBusy || !Api.available ? "disabled" : ""}>${state.vncDiscoveryBusy ? "Searching…" : "Refresh"}</button>
    </div>
    <div>${message}</div>
  </section>`;
}

function serversPage() {
  const groups = machinesByGroup();
  const empty = groups.length === 0;

  const row = (s) => `
    <div class="rx-table__row rx-table__row--body" data-server="${s.id}">
      <div class="rx-table__cell rx-table__cell--name">
        <div class="rx-body">${escapeHtml(s.name)}</div>
        <div class="rx-table__sub">${escapeHtml(s.comment || "")}</div>
      </div>
      <div class="rx-table__cell rx-table__cell--address rx-table__mono rx-body">${escapeHtml(s.address)}${s.port && s.port !== 22 ? ":" + s.port : ""}</div>
      <div class="rx-table__cell rx-table__cell--identity rx-body"
           style="color:var(--rx-ink-secondary)">${escapeHtml(s.identity || "Automatic")}</div>
      <div class="rx-table__cell rx-table__cell--cpu rx-table__cpu">
        <span class="rx-body rx-table__mono" style="color:var(--rx-ink-secondary)">—</span>
      </div>
      <div class="rx-table__cell rx-table__cell--last-used rx-body"
           style="color:var(--rx-ink-secondary)">${relativeTime(s.lastConnection)}</div>
      <div class="rx-table__cell rx-table__cell--actions">
        ${iconButton(icons.files, "Move to folder", `data-move-server="${s.id}"`)}
        ${s.transport === "uu-remote" ? "" : borderedButton("Files", "", true, `data-files="${s.id}"`)}
        ${borderedButton("Monitor", "", true, `data-monitor="${s.id}"`)}
        ${borderedButton("Connect", "", true, `data-connect="${s.id}"`)}
        ${["vnc", "uu-remote"].includes(s.remoteDesktopProtocol) ? borderedButton("Desktop", icons.windows, true, `data-desktop="${s.id}"`) : ""}
      </div>
    </div>`;

  const body = groups
    .map(
      (g) => `
      <div class="rx-table__group">
        <span>${escapeHtml(g.group)}</span>${g.source === "uuRemote" ? `<span class="rx-tag">UU Remote</span>` : ""}<span class="rx-spacer"></span>
        <span class="rx-axis">${g.items.length}</span>
        ${g.group === "Ungrouped" || g.linked ? "" : `<span class="rx-folder-actions">${plainButton("Rename", "", `data-rename-folder="${escapeHtml(g.group)}"`)}${plainButton("Delete", "", `data-remove-folder="${escapeHtml(g.group)}"`)}</span>`}
      </div>
      ${g.items.map(row).join("")}`
    )
    .join("");

  return `
    ${toolbar(
      "",
      `${borderedButton("New Folder", icons.files, true, "data-new-folder")} ${primaryButton("New Server", icons.plus, false, "data-new-server")}
       <label class="rx-field">${icons.search}
         <input placeholder="Search servers" data-search-servers />
       </label>`
    )}
    <div class="rx-page">
      <div class="rx-page-title-row"><h1 class="rx-page-title">Servers</h1></div>
      ${nearbyVncCard()}
      ${state.machinesError
        ? `<div class="rx-card rx-empty-state">
             <span class="rx-section-title">Can't read the saved servers</span>
             <span class="rx-help-text rx-help-text--error">${escapeHtml(state.machinesError)}</span>
           </div>`
        : empty
        ? `<div class="rx-card rx-empty-state">
             <span class="rx-section-title">No servers yet</span>
             <span class="rx-help-text">Add one with New Server.</span>
           </div>`
        : `<div class="rx-card" style="padding: var(--space-2);">
             <div class="rx-table">
               <div class="rx-table__row rx-table__row--head">
                 <div class="rx-table__cell rx-table__cell--name">Name</div>
                 <div class="rx-table__cell rx-table__cell--address">Address</div>
                 <div class="rx-table__cell rx-table__cell--identity">Identity</div>
                 <div class="rx-table__cell rx-table__cell--cpu">CPU</div>
                 <div class="rx-table__cell rx-table__cell--last-used">Last used</div>
                 <div class="rx-table__cell rx-table__cell--actions"></div>
               </div>
               ${body}
             </div>
           </div>`}
    </div>`;
}

function remoteDesktopPage() {
  const machine = allMachines().find(item => item.id === state.desktopMachineId);
  const isUU = machine?.remoteDesktopProtocol === "uu-remote";
  return `
    ${toolbar(
      "",
      `${borderedButton("Servers", icons.servers, true, "data-desktop-back")} ${plainButton("Stop Desktop", "", "data-desktop-stop")}`
    )}
    <div class="rx-page">
      <div class="rx-page-title-row">
        <h1 class="rx-page-title">${escapeHtml(machine?.name ?? "Remote Desktop")}</h1>
        <span class="rx-spacer"></span>
        <span class="rx-help-text" data-vnc-status>Connecting…</span>
      </div>
      <div class="rx-help-text rx-help-text--error" data-vnc-error>${escapeHtml(state.desktopError ?? "")}</div>
      <div class="rx-card" style="display:flex;align-items:center;justify-content:center;min-height:320px;padding:var(--space-3);background:#111;overflow:auto">
        <video data-uu-video tabindex="0" aria-label="UU Remote screen" autoplay playsinline muted style="${isUU ? "" : "display:none;"}max-width:100%;max-height:calc(100vh - 220px);object-fit:contain;outline:none;touch-action:none"></video>
        <canvas data-vnc-canvas tabindex="0" aria-label="Remote desktop screen" style="${isUU ? "display:none;" : ""}max-width:100%;max-height:calc(100vh - 220px);object-fit:contain;outline:none;touch-action:none"></canvas>
      </div>
    </div>`;
}

function terminalPage() {
  const machine = allMachines().find(item => item.id === state.terminalMachineId);
  return `
    ${toolbar("", `${borderedButton("Servers", icons.servers, true, "data-terminal-back")} ${plainButton("Close Terminal", "", "data-terminal-stop")}`)}
    <div class="rx-page">
      <div class="rx-page-title-row">
        <h1 class="rx-page-title">${escapeHtml(machine?.name ?? "Terminal")}</h1>
        <span class="rx-spacer"></span><span class="rx-help-text" data-terminal-status>${escapeHtml(state.terminalPhase ?? "connecting")}</span>
      </div>
      <div class="rx-help-text rx-help-text--error" data-terminal-error>${escapeHtml(state.terminalError ?? "")}</div>
      <div class="rx-card" style="padding:0;background:#111;overflow:hidden">
        <pre data-terminal-output tabindex="0" aria-label="Terminal output" style="height:calc(100vh - 310px);min-height:280px;overflow:auto;margin:0;padding:16px;color:#e6e6e6;background:#111;font:13px/1.5 ui-monospace,SFMono-Regular,Consolas,monospace;white-space:pre-wrap;overflow-wrap:anywhere">${escapeHtml(state.terminalOutput)}</pre>
        <form data-terminal-form style="display:flex;gap:8px;padding:12px;border-top:1px solid #333">
          <textarea name="input" data-terminal-input rows="1" autocomplete="off" autocapitalize="off" spellcheck="false" aria-label="Terminal input" style="flex:1;resize:vertical;min-height:36px;max-height:120px;padding:8px;color:#f3f3f3;background:#181818;border:1px solid #444;border-radius:6px;font:13px/1.5 ui-monospace,SFMono-Regular,Consolas,monospace"></textarea>
          <button class="rx-button rx-button--primary" type="submit" data-terminal-send>Send</button>
        </form>
      </div>
    </div>`;
}

function filesPage() {
  const rows = (state.fileEntries ?? []).map(entry => `
    <div class="rx-table__row rx-table__row--body" style="display:grid;grid-template-columns:minmax(220px,1fr) 120px 180px 120px">
      <div class="rx-table__cell rx-body">${entry.isDirectory
        ? `<button class="rx-button rx-button--plain" type="button" data-file-directory="${escapeHtml(entry.fullName)}">${icons.files} ${escapeHtml(entry.name)}</button>`
        : escapeHtml(entry.name)}</div>
      <div class="rx-table__cell rx-body rx-table__mono">${entry.isDirectory ? "Folder" : formatFileSize(entry.length)}</div>
      <div class="rx-table__cell rx-body">${escapeHtml(entry.lastWriteTimeUtc ? new Date(entry.lastWriteTimeUtc).toLocaleString() : "")}</div>
      <div class="rx-table__cell rx-body">${entry.isDirectory ? "" : borderedButton("Download", "", true, `data-file-download="${escapeHtml(entry.fullName)}"`)}</div>
    </div>`).join("");
  return `
    ${toolbar("", `${borderedButton("Servers", icons.servers, true, "data-files-back")} ${plainButton("New Folder", icons.files, "data-files-new-folder")} <label class="rx-button rx-button--plain">Upload<input type="file" data-file-upload multiple hidden /></label>`)}
    <div class="rx-page">
      <div class="rx-page-title-row"><h1 class="rx-page-title">${escapeHtml(allMachines().find(item => item.id === state.fileMachineId)?.name ?? "Files")}</h1><span class="rx-spacer"></span></div>
      <div class="rx-row-3" style="margin-bottom:var(--space-3)">
        <button class="rx-button rx-button--plain" type="button" data-files-up>Up</button>
        <form data-files-path-form style="display:flex;flex:1;gap:8px">
          <span class="rx-field" style="flex:1"><input name="path" data-files-path value="${escapeHtml(state.filePath)}" autocomplete="off" /></span>
          <button class="rx-button rx-button--plain" type="submit">Open</button>
        </form>
        <button class="rx-button rx-button--plain" type="button" data-files-refresh>${state.fileBusy ? "Loading…" : "Refresh"}</button>
      </div>
      <div class="rx-help-text rx-help-text--error" data-files-error>${escapeHtml(state.fileError ?? "")}</div>
      <div class="rx-card" style="padding:var(--space-2);overflow:auto">
        <div class="rx-table__row rx-table__row--head" style="display:grid;grid-template-columns:minmax(220px,1fr) 120px 180px 120px"><div class="rx-table__cell">Name</div><div class="rx-table__cell">Size</div><div class="rx-table__cell">Modified</div><div class="rx-table__cell"></div></div>
        ${rows || `<p class="rx-help-text" style="padding:var(--space-3)">${state.fileBusy ? "Loading files…" : "This folder is empty."}</p>`}
      </div>
    </div>`;
}

function formatFileSize(value) {
  const size = Number(value) || 0;
  if (size < 1024) return `${size} B`;
  if (size < 1024 * 1024) return `${(size / 1024).toFixed(1)} KB`;
  if (size < 1024 * 1024 * 1024) return `${(size / (1024 * 1024)).toFixed(1)} MB`;
  return `${(size / (1024 * 1024 * 1024)).toFixed(1)} GB`;
}

function identitiesPage() {
  const list = state.identities === null ? [] : state.identities;

  const rows = list
    .map(
      (i) => `
      <div style="display:flex;align-items:center;gap:12px;height:56px;padding:0 16px;">
        <span class="rx-symbol-tile">${icons.identities}</span>
        <div style="display:flex;flex-direction:column;gap:2px;min-width:0;">
          <span class="rx-body">${escapeHtml(i.username)}</span>
          <span class="rx-help" style="color:var(--rx-ink-tertiary)">${escapeHtml(i.keyType)}${i.automatic ? " · automatic" : ""}</span>
        </div>
        <span class="rx-spacer"></span>
        ${i.hasSecret ? "" : `<span class="rx-tag rx-tag--warning">no secret</span>`}
        ${plainButton("Remove", "", `data-remove-identity="${i.id}"`)}
      </div>`
    )
    .join("");

  return `
    ${toolbar("", primaryButton("New Identity", icons.plus, false, "data-new-identity"))}
    <div class="rx-page">
      <div class="rx-page-title-row"><h1 class="rx-page-title">Identities</h1></div>
      ${state.identitiesError
        ? `<div class="rx-card rx-empty-state">
             <span class="rx-section-title">Can't read the saved identities</span>
             <span class="rx-help-text rx-help-text--error">${escapeHtml(state.identitiesError)}</span>
           </div>`
        : list.length === 0
        ? `<div class="rx-card rx-empty-state">
             <span class="rx-section-title">No identities yet</span>
             <span class="rx-help-text">Add a password or key with New Identity. Servers set to Automatic try these in turn.</span>
           </div>`
        : `<div class="rx-card rx-card--chrome">
             <div class="rx-divided-stack">${rows}</div>
           </div>`}
    </div>`;
}

function snippetsPage() {
  return `
    ${toolbar("", primaryButton("New Snippet"))}
    <div class="rx-page">
      <div class="rx-page-title-row"><h1 class="rx-page-title">Snippets</h1></div>
      <div class="rx-card rx-card--chrome">
        <div class="rx-divided-stack">
          ${snippets
            .map(
              (s) => `
            <div style="display:flex;align-items:center;gap:12px;height:64px;padding:0 16px;">
              <span class="rx-symbol-tile">${icons.snippets}</span>
              <div style="display:flex;flex-direction:column;gap:4px;min-width:0;">
                <span class="rx-body">${s.name}</span>
                <span class="rx-code" style="color:var(--rx-ink-tertiary)">${s.command}</span>
              </div>
              <span class="rx-spacer"></span>
              <span class="rx-tag">${s.badge}</span>
              ${plainButton("Run")}
            </div>`
            )
            .join("")}
        </div>
      </div>
    </div>`;
}

function portForwardPage() {
  return `
    ${toolbar("", primaryButton("New Forward"))}
    <div class="rx-page">
      <div class="rx-page-title-row"><h1 class="rx-page-title">Port Forward</h1></div>
      <div class="rx-card rx-card--chrome">
        <div class="rx-divided-stack">
          ${portForwards
            .map(
              (p) => `
            <div style="display:flex;align-items:center;gap:12px;height:56px;padding:0 16px;">
              <span class="rx-status-dot rx-status-dot--${p.state}"></span>
              <span class="rx-body" style="width:120px">${p.label}</span>
              <span class="rx-code">${p.local}</span>
              <span class="rx-hint-text">→</span>
              <span class="rx-code">${p.remote}</span>
              <span class="rx-spacer"></span>
              ${plainButton(p.state === "running" ? "Stop" : "Start")}
            </div>`
            )
            .join("")}
        </div>
      </div>
    </div>`;
}

function uuRemoteCard() {
  const account = state.uuRemote ?? {};
  const error = state.uuError || account.loginError || account.refreshError;
  const deviceRows = (account.devices ?? [])
    .filter(device => device.desktop && device.deviceID !== account.ownDeviceID)
    .map(device => `
      <div class="uu-account__device">
        <span class="rx-status-dot ${device.online ? "rx-status-dot--success" : "rx-status-dot--off"}"></span>
        <div class="uu-account__device-copy">
          <strong>${escapeHtml(device.alias || device.deviceID)}</strong>
          <span>${escapeHtml(device.platformName)} · ${device.online ? "Online" : "Offline"}</span>
        </div>
      </div>`).join("");

  let content;
  if (!Api.available) {
    content = `<p class="rx-help-text">Sign in from the Windows app to sync your UU Remote computers into their own server folder.</p>`;
  } else if (account.isLoggedIn) {
    content = `
      <div class="uu-account__identity">
        <div><strong>${escapeHtml(account.nickname || "UU Remote account")}</strong><span>${escapeHtml(account.userID || "Signed in")}</span></div>
        <button class="rx-button rx-button--plain" type="button" data-uu-refresh ${state.uuBusy ? "disabled" : ""}>Refresh devices</button>
        <button class="rx-button rx-button--plain" type="button" data-uu-logout ${state.uuBusy ? "disabled" : ""}>Sign out</button>
      </div>
      <p class="rx-help-text">Desktop devices are kept in the linked “UU Remote” folder.</p>
      <div class="uu-account__devices">${deviceRows || `<p class="rx-help-text">No other desktop devices are linked to this account.</p>`}</div>`;
  } else {
    const modes = [["qr", "QR code"], ["sms", "SMS"], ["token", "Token"]]
      .map(([id, label]) => `<button type="button" class="rx-button ${state.uuMode === id ? "rx-button--primary" : "rx-button--plain"}" data-uu-mode="${id}">${label}</button>`).join("");
    let loginFields;
    if (state.uuMode === "sms") {
      loginFields = `
        <form class="uu-account__form" data-uu-sms-send>
          <label class="rx-stack-1"><span class="rx-caps-label">Country calling code</span><span class="rx-field"><input name="countryCode" value="${escapeHtml(state.uuCountryCode)}" required /></span></label>
          <label class="rx-stack-1"><span class="rx-caps-label">Mobile number</span><span class="rx-field"><input name="mobile" value="${escapeHtml(state.uuMobile)}" inputmode="tel" required /></span></label>
          <button class="rx-button rx-button--plain" type="submit" ${state.uuBusy ? "disabled" : ""}>Send verification code</button>
        </form>
        <form class="uu-account__form" data-uu-sms-login>
          <label class="rx-stack-1"><span class="rx-caps-label">Verification code</span><span class="rx-field"><input name="code" value="${escapeHtml(state.uuSmsCode)}" inputmode="numeric" required /></span></label>
          <button class="rx-button rx-button--primary" type="submit" ${state.uuBusy ? "disabled" : ""}>Sign in</button>
        </form>`;
    } else if (state.uuMode === "token") {
      loginFields = `
        <form class="uu-account__form" data-uu-token-login>
          <label class="rx-stack-1"><span class="rx-caps-label">UU Remote token</span><span class="rx-field"><input name="token" type="password" autocomplete="off" required value="${escapeHtml(state.uuTokenDraft)}" /></span></label>
          <details><summary>Additional account values</summary>
            <div class="uu-account__form uu-account__form--details">
              <label class="rx-stack-1"><span class="rx-caps-label">User ID</span><span class="rx-field"><input name="userID" value="${escapeHtml(state.uuTokenUserID)}" /></span></label>
              <label class="rx-stack-1"><span class="rx-caps-label">Device ID</span><span class="rx-field"><input name="deviceID" value="${escapeHtml(state.uuTokenDeviceID)}" /></span></label>
              <label class="rx-stack-1"><span class="rx-caps-label">Client ID</span><span class="rx-field"><input name="clientID" value="${escapeHtml(state.uuTokenClientID)}" /></span></label>
              <label class="rx-stack-1"><span class="rx-caps-label">System ID</span><span class="rx-field"><input name="systemID" value="${escapeHtml(state.uuTokenSystemID)}" /></span></label>
            </div>
          </details>
          <button class="rx-button rx-button--primary" type="submit" ${state.uuBusy ? "disabled" : ""}>Import token</button>
        </form>`;
    } else {
      const active = ["waiting", "scanned", "confirmed", "generating"].includes(account.qrState);
      loginFields = `
        <div class="uu-account__qr">
          ${state.uuQrImage ? `<img src="${escapeHtml(state.uuQrImage)}" alt="UU Remote sign-in QR code" />` : `<div class="uu-account__qr-empty">QR code appears here</div>`}
          <div class="uu-account__qr-copy"><strong>${escapeHtml(account.qrHint || "Sign in by scanning a QR code")}</strong><span>Scan with the UU Remote app and confirm the sign-in.</span></div>
        </div>
        <button class="rx-button rx-button--primary" type="button" data-uu-qr-start ${state.uuBusy || active ? "disabled" : ""}>${state.uuQrImage ? "Generate a new QR code" : "Generate QR code"}</button>`;
    }
    content = `
      <p class="rx-help-text">Sign in to keep your UU Remote desktop devices synced in a separate server folder.</p>
      <div class="uu-account__modes">${modes}</div>
      ${loginFields}
      ${state.uuNotice ? `<p class="rx-help-text">${escapeHtml(state.uuNotice)}</p>` : ""}`;
  }

  return `<section class="rx-card uu-account">
    <div class="rx-section-title-row"><span class="rx-section-title">UU Remote</span></div>
    ${content}
    ${error ? `<p class="rx-help-text rx-help-text--error">${escapeHtml(error)}</p>` : ""}
  </section>`;
}

function settingsPage() {
  return `
    ${toolbar("", "")}
    <div class="rx-page">
      <div class="rx-page-title-row"><h1 class="rx-page-title">Settings</h1></div>
      ${uuRemoteCard()}
      <div class="rx-card">
        <div class="rx-section-title-row" style="margin-bottom:8px">
          <span class="rx-section-title">Appearance</span>
        </div>
        <div class="rx-divided-stack">
          <div class="rx-form-row">
            <div class="rx-form-row__labels">
              <span class="rx-form-row__title">Dark mode</span>
              <span class="rx-help-text">Follows the system appearance by default.</span>
            </div>
            <button class="rx-switch${state.dark ? " rx-switch--on" : ""}" data-toggle-dark aria-label="Dark mode"></button>
          </div>
          <div class="rx-form-row">
            <div class="rx-form-row__labels">
              <span class="rx-form-row__title">Accent</span>
              <span class="rx-help-text">catussh indigo: selection, links and focus.</span>
            </div>
            <div class="rx-row-2">
              <span class="rx-status-dot rx-status-dot--regular" style="background:var(--rx-accent);width:16px;height:16px"></span>
              <span class="rx-code">#4B49C8</span>
            </div>
          </div>
        </div>
      </div>
    </div>`;
}

let uuQrPollPromise = null;

function beginUUQrPolling() {
  if (uuQrPollPromise) return uuQrPollPromise;
  uuQrPollPromise = (async () => {
    while (["waiting", "scanned", "confirmed"].includes(state.uuRemote?.qrState) && !state.uuRemote?.isLoggedIn) {
      const result = await Api.pollUuQr();
      if (!result || result.error) {
        state.uuError = result?.error ?? "Could not check the UU Remote sign-in.";
        state.uuRemote = { ...(state.uuRemote ?? {}), qrState: "failed", qrHint: "Sign-in status could not be checked." };
        if (state.page === "settings") render();
        break;
      }
      if (result.account) state.uuRemote = result.account;
      state.uuError = null;
      if (state.uuRemote?.isLoggedIn) {
        await loadMachines();
        break;
      }
      if (result.status === 0) break;
      if (state.page === "settings") render();
    }
  })().finally(() => { uuQrPollPromise = null; });
  return uuQrPollPromise;
}

document.addEventListener("click", async (event) => {
  const mode = event.target.closest("[data-uu-mode]");
  if (mode) {
    event.preventDefault();
    state.uuMode = mode.dataset.uuMode;
    state.uuError = null;
    render();
    return;
  }

  if (event.target.closest("[data-uu-qr-start]")) {
    event.preventDefault();
    state.uuBusy = true;
    state.uuError = null;
    state.uuNotice = null;
    render();
    const result = await Api.startUuQr();
    state.uuBusy = false;
    if (!result || result.error) {
      state.uuError = result?.error ?? "Could not start UU Remote sign-in.";
    } else {
      state.uuQrImage = result.qrImage;
      state.uuRemote = result.account;
      render();
      beginUUQrPolling();
      return;
    }
    render();
    return;
  }

  if (event.target.closest("[data-uu-refresh]")) {
    event.preventDefault();
    state.uuBusy = true;
    state.uuError = null;
    render();
    const result = await Api.refreshUuDevices();
    state.uuBusy = false;
    if (!result || result.error) state.uuError = result?.error ?? "Could not refresh UU Remote devices.";
    else { state.uuRemote = result; state.uuError = null; }
    await loadMachines();
    return;
  }

  if (event.target.closest("[data-uu-logout]")) {
    event.preventDefault();
    state.uuBusy = true;
    state.uuError = null;
    render();
    const result = await Api.logoutUu();
    state.uuBusy = false;
    if (!result || result.error) state.uuError = result?.error ?? "Could not sign out of UU Remote.";
    else { state.uuRemote = result; state.uuQrImage = null; state.uuError = null; }
    await loadMachines();
  }
});

document.addEventListener("submit", async (event) => {
  const form = event.target.closest("[data-uu-sms-send], [data-uu-sms-login], [data-uu-token-login]");
  if (!form) return;
  event.preventDefault();
  const data = new FormData(form);
  state.uuError = null;
  state.uuNotice = null;

  if (form.matches("[data-uu-sms-send]")) {
    state.uuCountryCode = data.get("countryCode")?.toString().trim() ?? "";
    state.uuMobile = data.get("mobile")?.toString().trim() ?? "";
    state.uuBusy = true;
    render();
    const result = await Api.sendUuSms({ countryCode: state.uuCountryCode, mobile: state.uuMobile });
    state.uuBusy = false;
    if (result?.error) state.uuError = result.error;
    else state.uuNotice = "Verification code sent.";
    render();
    return;
  }

  if (form.matches("[data-uu-sms-login]")) {
    state.uuSmsCode = data.get("code")?.toString().trim() ?? "";
    state.uuBusy = true;
    render();
    const result = await Api.loginUuSms({ countryCode: state.uuCountryCode, mobile: state.uuMobile, code: state.uuSmsCode });
    state.uuBusy = false;
    if (result?.error) state.uuError = result.error;
    else {
      state.uuRemote = result;
      state.uuSmsCode = "";
      state.uuError = null;
      await loadMachines();
      return;
    }
    render();
    return;
  }

  state.uuTokenDraft = data.get("token")?.toString().trim() ?? "";
  state.uuTokenUserID = data.get("userID")?.toString().trim() ?? "";
  state.uuTokenDeviceID = data.get("deviceID")?.toString().trim() ?? "";
  state.uuTokenClientID = data.get("clientID")?.toString().trim() ?? "";
  state.uuTokenSystemID = data.get("systemID")?.toString().trim() ?? "";
  state.uuBusy = true;
  render();
  const result = await Api.importUuToken({
    token: state.uuTokenDraft,
    userID: state.uuTokenUserID,
    deviceID: state.uuTokenDeviceID,
    clientID: state.uuTokenClientID,
    systemID: state.uuTokenSystemID,
  });
  state.uuBusy = false;
  if (result?.error) state.uuError = result.error;
  else {
    state.uuRemote = result;
    state.uuTokenDraft = "";
    state.uuError = null;
    await loadMachines();
    return;
  }
  render();
});

/*
 * Monitor is not a sidebar entry: in the Swift app it is a session you open from
 * a server's Monitor button, and the sidebar keeps the Server group. It is
 * reachable by hash (#monitor) and from the per-server buttons.
 */
const renderers = {
  home: homePage,
  servers: serversPage,
  identities: identitiesPage,
  snippets: snippetsPage,
  portforward: portForwardPage,
  settings: settingsPage,
  monitor: () => monitorPage(state),
  desktop: remoteDesktopPage,
  terminal: terminalPage,
  files: filesPage,
};

/* ---------------------------------------------------------------- router */

function render() {
  document.documentElement.classList.toggle("dark", state.dark);
  const page = renderers[state.page] || homePage;
  const content = document.getElementById("app");
  content.innerHTML = `
    <div class="rx-window">
      ${sidebar()}
      <main class="rx-content">${page()}</main>
    </div>
    ${sheetMarkup()}`;

  // Toggles and the monitor's refresh timer are per-render, not per-page-lifetime.
  if (state.page === "monitor") {
    mountMonitor(content);
    ensureMonitorPolling();
    // Direct navigation (#monitor on load) never goes through setPage, so the
    // first reading would otherwise wait a full polling interval.
    if (state.monitor === null) refreshMonitor();
  } else {
    stopMonitorPolling();
  }
  if (state.page === "desktop") {
    const machine = allMachines().find(item => item.id === state.desktopMachineId);
    const isUU = machine?.remoteDesktopProtocol === "uu-remote";
    if (isUU) mountUuDesktop(content); else mountDesktop(content);
    ensureDesktopPolling();
    if (!Api.available) {
      state.desktopError = "Open this page from the catussh desktop app to connect.";
      updateDesktopStatus();
    } else if (state.machines !== null && state.desktopMachineId && state.desktopStartedId !== state.desktopMachineId) {
      if (!machine) {
        state.desktopPhase = "failed";
        state.desktopError = "No such server.";
        updateDesktopStatus();
      } else {
        const id = state.desktopMachineId;
        state.desktopStartedId = id;
        Api.startDesktop(id, "desktop").then(async result => {
          if (state.desktopMachineId !== id) return;
          if (result?.error) state.desktopError = result.error;
          if (isUU && result?.bootstrap) await startUuViewer(id, "desktop", result);
          refreshDesktop();
        });
      }
    }
    refreshDesktop();
  } else {
    stopDesktopPolling();
  }

  if (state.page === "terminal") {
    mountTerminal(content);
    ensureTerminalPolling();
    if (!Api.available) {
      state.terminalError = "Open this page from the catussh desktop app to connect.";
      updateTerminalStatus();
    } else if (state.machines !== null && state.terminalMachineId && state.terminalStartedId !== state.terminalMachineId) {
      if (!allMachines().some(item => item.id === state.terminalMachineId)) {
        state.terminalPhase = "failed";
        state.terminalError = "No such server.";
        updateTerminalStatus();
      } else {
        state.terminalStartedId = state.terminalMachineId;
        void startTerminalSession(state.terminalMachineId);
      }
    }
  } else {
    stopTerminalPolling();
    if (state.page !== "desktop") window.UUViewer.close();
  }

  if (state.page === "files" && state.fileMachineId && !state.fileLoaded && !state.fileBusy) void loadFiles();
  if (state.page === "servers") {
    ensureVncDiscoveryPolling();
    if (!state.vncDiscoveryLoaded && !state.vncDiscoveryBusy) void loadVncDiscovery();
  } else stopVncDiscoveryPolling();
}

function setPage(id) {
  state.page = id;
  render();
  if (id === "monitor") refreshMonitor();
}

/* ------------------------------------------------------- monitor wiring */

let monitorTimer = null;

/** The monitor refreshes once a second, the cadence of the merged probe's sleep. */
function ensureMonitorPolling() {
  if (monitorTimer !== null) return;
  monitorTimer = setInterval(refreshMonitor, 1000);
}

function stopMonitorPolling() {
  if (monitorTimer === null) return;
  clearInterval(monitorTimer);
  monitorTimer = null;
}

let desktopTimer = null;
let desktopRefreshBusy = false;
let desktopInputQueue = Promise.resolve();
let uuViewerKey = null;
let uuViewerStart = Promise.resolve();
let uuSignalQueue = Promise.resolve();
let terminalTimer = null;
let terminalInputQueue = Promise.resolve();
let terminalDecoder = new TextDecoder();
let vncDiscoveryTimer = null;


function ensureDesktopPolling() {
  if (desktopTimer !== null) return;
  desktopTimer = setInterval(refreshDesktop, 250);
}

function stopDesktopPolling() {
  if (desktopTimer === null) return;
  clearInterval(desktopTimer);
  desktopTimer = null;
}

function updateDesktopStatus() {
  const status = document.querySelector("[data-vnc-status]");
  const error = document.querySelector("[data-vnc-error]");
  if (status) status.textContent = state.desktopPhase || "connecting";
  if (error) error.textContent = state.desktopError || "";
}

function paintDesktopFrame(frame) {
  const canvas = document.querySelector("[data-vnc-canvas]");
  if (!canvas || !frame) return;
  if (canvas.width !== frame.width || canvas.height !== frame.height) {
    canvas.width = frame.width;
    canvas.height = frame.height;
  }
  const binary = atob(frame.rgba);
  const pixels = new Uint8ClampedArray(binary.length);
  for (let i = 0; i < binary.length; i++) pixels[i] = binary.charCodeAt(i);
  canvas.getContext("2d").putImageData(new ImageData(pixels, frame.width, frame.height), 0, 0);
}

function closeUUViewer() {
  window.UUViewer.close();
  uuViewerKey = null;
  uuViewerStart = Promise.resolve();
  uuSignalQueue = Promise.resolve();
}

async function startUuViewer(id, purpose, snapshot) {
  const key = `${id}:${purpose}`;
  if (uuViewerKey !== key) {
    closeUUViewer();
    uuViewerKey = key;
    const updateState = phase => {
      if (state.page === "desktop" && state.desktopMachineId === id) {
        state.desktopPhase = phase;
        updateDesktopStatus();
      } else if (state.page === "terminal" && state.terminalMachineId === id) {
        state.terminalPhase = phase;
        updateTerminalStatus();
      }
    };
    const updateError = error => {
      if (state.page === "desktop" && state.desktopMachineId === id) {
        state.desktopError = error;
        updateDesktopStatus();
      } else if (state.page === "terminal" && state.terminalMachineId === id) {
        state.terminalError = error;
        updateTerminalStatus();
      }
    };
    uuViewerStart = window.UUViewer.connect(snapshot.bootstrap, {
      id,
      purpose,
      signal: Api.desktopSignal,
      onState: updateState,
      onError: updateError,
      onTerminalOutput: appendTerminalOutput,
      onTerminalExit: code => { state.terminalPhase = `closed (exit ${code ?? "?"})`; updateTerminalStatus(); },
    });
  }
  try { await uuViewerStart; }
  catch (error) {
    if (uuViewerKey === key) closeUUViewer();
    throw error;
  }
  state.desktopSignalVersion = Math.max(state.desktopSignalVersion, snapshot.version ?? 0);
  if (snapshot.signals?.length) {
    uuSignalQueue = uuSignalQueue.then(() => window.UUViewer.handleSignals(snapshot.signals)).catch(error => {
      if (uuViewerKey === key) {
        if (state.page === "desktop") { state.desktopError = String(error); updateDesktopStatus(); }
        if (state.page === "terminal") { state.terminalError = String(error); updateTerminalStatus(); }
      }
    });
    await uuSignalQueue;
  }
}

async function refreshDesktop() {
  const id = state.desktopMachineId;
  if (state.page !== "desktop" || !id || !Api.available || state.machines === null || desktopRefreshBusy) return;
  desktopRefreshBusy = true;
  try {
    const machine = allMachines().find(item => item.id === id);
    if (!machine) {
      state.desktopPhase = "failed";
      state.desktopError = "No such server.";
      updateDesktopStatus();
      return;
    }
    const isUU = machine?.remoteDesktopProtocol === "uu-remote";
    const result = await Api.desktop(id, isUU ? state.desktopSignalVersion : state.desktopFrameVersion);
    if (state.page !== "desktop" || state.desktopMachineId !== id) return;
    if (!result || result.error) {
      state.desktopPhase = "failed";
      state.desktopError = result?.error ?? "Could not reach the host.";
    } else {
      state.desktopPhase = result.phase ?? "connecting";
      state.desktopError = result.error ?? null;
      if (isUU) {
        state.desktopSignalVersion = Math.max(state.desktopSignalVersion, result.version ?? 0);
        if (result.bootstrap) await startUuViewer(id, "desktop", result);
      } else {
        state.desktopFrameVersion = result.frameVersion ?? state.desktopFrameVersion;
        if (result.frame) {
          state.desktopFrame = result.frame;
          paintDesktopFrame(result.frame);
        }
      }
    }
    updateDesktopStatus();
  } catch (error) {
    if (state.page === "desktop" && state.desktopMachineId === id) {
      state.desktopPhase = "failed";
      state.desktopError = String(error);
      updateDesktopStatus();
    }
  } finally {
    desktopRefreshBusy = false;
  }
}

function ensureTerminalPolling() {
  if (terminalTimer !== null) return;
  terminalTimer = setInterval(refreshTerminal, 150);
}

function stopTerminalPolling() {
  if (terminalTimer === null) return;
  clearInterval(terminalTimer);
  terminalTimer = null;
}

async function startTerminalSession(id) {
  const machine = allMachines().find(item => item.id === id);
  if (!machine) { state.terminalError = "No such server."; updateTerminalStatus(); return; }
  state.terminalTransport = machine.transport === "uu-remote" ? "uu-remote" : "ssh";
  try {
    if (state.terminalTransport === "uu-remote") {
      const result = await Api.startDesktop(id, "terminal");
      if (state.page !== "terminal" || state.terminalMachineId !== id) return;
      if (!result || result.error) throw new Error(result?.error ?? "Could not start the UU Remote terminal.");
      state.terminalPhase = result.phase ?? "connecting";
      state.terminalError = null;
      state.desktopSignalVersion = 0;
      if (result.bootstrap) await startUuViewer(id, "terminal", result);
    } else {
      const result = await Api.startTerminal(id);
      if (state.page !== "terminal" || state.terminalMachineId !== id) return;
      if (!result || result.error) throw new Error(result?.error ?? "Could not start the SSH terminal.");
      state.terminalPhase = "connected";
      state.terminalError = null;
      if (result.banner) appendTerminalOutput(`${result.banner}\r\n`);
      resizeTerminalView();
    }
    updateTerminalStatus();
    refreshTerminal();
  } catch (error) {
    state.terminalPhase = "failed";
    state.terminalError = String(error);
    updateTerminalStatus();
  }
}

async function refreshTerminal() {
  const id = state.terminalMachineId;
  if (state.page !== "terminal" || !id || !Api.available || !state.terminalTransport || state.terminalPollBusy) return;
  state.terminalPollBusy = true;
  try {
    if (state.terminalTransport === "uu-remote") {
      const result = await Api.desktop(id, state.desktopSignalVersion);
      if (state.page !== "terminal" || state.terminalMachineId !== id) return;
      if (!result || result.error) throw new Error(result?.error ?? "UU Remote terminal disconnected.");
      state.terminalPhase = result.phase ?? state.terminalPhase;
      state.terminalError = null;
      state.desktopSignalVersion = Math.max(state.desktopSignalVersion, result.version ?? 0);
      if (result.bootstrap) await startUuViewer(id, "terminal", result);
    } else {
      const result = await Api.terminal(id);
      if (state.page !== "terminal" || state.terminalMachineId !== id) return;
      if (!result || result.error) throw new Error(result?.error ?? "SSH terminal disconnected.");
      state.terminalPhase = result.phase ?? "connected";
      if (result.output) appendTerminalOutput(terminalDecoder.decode(Uint8Array.from(atob(result.output), char => char.charCodeAt(0)), { stream: true }));
    }
    updateTerminalStatus();
  } catch (error) {
    if (state.page === "terminal" && state.terminalMachineId === id) {
      state.terminalPhase = "failed";
      state.terminalError = String(error);
      updateTerminalStatus();
    }
  } finally {
    state.terminalPollBusy = false;
  }
}

function appendTerminalOutput(text) {
  if (!text) return;
  state.terminalOutput = (state.terminalOutput + text).slice(-100000);
  const output = document.querySelector("[data-terminal-output]");
  if (output) {
    output.textContent += text;
    if (output.textContent.length > 100000) output.textContent = output.textContent.slice(-100000);
    output.scrollTop = output.scrollHeight;
  }
}

function updateTerminalStatus() {
  const status = document.querySelector("[data-terminal-status]");
  const error = document.querySelector("[data-terminal-error]");
  const input = document.querySelector("[data-terminal-input]");
  const send = document.querySelector("[data-terminal-send]");
  const connected = state.terminalPhase === "connected";
  if (status) status.textContent = state.terminalPhase || "connecting";
  if (error) error.textContent = state.terminalError || "";
  if (input) input.disabled = !connected;
  if (send) send.disabled = !connected;
}

function queueTerminalRequest(id, action) {
  terminalInputQueue = terminalInputQueue.then(async () => {
    if (state.page !== "terminal" || state.terminalMachineId !== id) return;
    const result = await action();
    if (result?.error) throw new Error(result.error);
  }).catch(error => {
    if (state.page === "terminal" && state.terminalMachineId === id) {
      state.terminalPhase = "failed";
      state.terminalError = String(error);
      updateTerminalStatus();
    }
  });
}

function resizeTerminalView() {
  const output = document.querySelector("[data-terminal-output]");
  if (!output || !state.terminalMachineId || !state.terminalTransport) return;
  const id = state.terminalMachineId;
  const columns = Math.max(20, Math.floor((output.clientWidth - 32) / 8));
  const rows = Math.max(5, Math.floor(output.clientHeight / 19));
  if (state.terminalTransport === "uu-remote") window.UUViewer.resizeTerminal(columns, rows);
  else if (state.terminalPhase === "connected") queueTerminalRequest(id, () => Api.resizeTerminal(id, columns, rows));
}

function sendTerminalText(text) {
  if (!text || !state.terminalMachineId || state.terminalPhase !== "connected") return;
  const id = state.terminalMachineId;
  if (state.terminalTransport === "uu-remote") window.UUViewer.sendTerminalInput(text);
  else if (state.terminalTransport === "ssh") queueTerminalRequest(id, () => Api.sendTerminal(id, text));
}

function mountTerminal(content) {
  const input = content.querySelector("[data-terminal-input]");
  if (!input) return;
  const output = content.querySelector("[data-terminal-output]");
  if (output) output.scrollTop = output.scrollHeight;
  updateTerminalStatus();
  input.addEventListener("input", () => {
    const text = input.value;
    input.value = "";
    sendTerminalText(text);
  });
  input.addEventListener("keydown", event => {
    const keys = { Enter: "\r", Backspace: "\x7f", Tab: "\t", Escape: "\x1b", ArrowUp: "\x1b[A", ArrowDown: "\x1b[B", ArrowRight: "\x1b[C", ArrowLeft: "\x1b[D", Delete: "\x1b[3~", Home: "\x1b[H", End: "\x1b[F" };
    let text = keys[event.key];
    if (event.ctrlKey && event.key.toLowerCase() === "c") text = "\x03";
    if (event.ctrlKey && event.key.toLowerCase() === "d") text = "\x04";
    if (text !== undefined) {
      event.preventDefault();
      sendTerminalText(text);
    }
  });
  input.focus();
  window.onresize = resizeTerminalView;
}

function sendDesktopInput(send) {
  desktopInputQueue = desktopInputQueue.then(send).catch(() => {});
}

function keySymFor(event) {
  const special = {
    Backspace: 0xff08, Tab: 0xff09, Enter: 0xff0d, Escape: 0xff1b, Delete: 0xffff,
    Home: 0xff50, ArrowLeft: 0xff51, ArrowUp: 0xff52, ArrowRight: 0xff53, ArrowDown: 0xff54,
    PageUp: 0xff55, PageDown: 0xff56, End: 0xff57, Insert: 0xff63,
    Shift: 0xffe1, Control: 0xffe3, Alt: 0xffe9, Meta: 0xffeb,
  };
  if (special[event.key]) return special[event.key];
  if (/^F([1-9]|1[0-2])$/.test(event.key)) return 0xffbd + Number(event.key.slice(1));
  if (event.key.length === 1) {
    const code = event.key.codePointAt(0);
    return code <= 0xff ? code : 0x01000000 | code;
  }
  return null;
}

function mountDesktop(content) {
  const canvas = content.querySelector("[data-vnc-canvas]");
  if (!canvas) return;
  if (state.desktopFrame) paintDesktopFrame(state.desktopFrame);
  updateDesktopStatus();

  const machineId = state.desktopMachineId;
  let lastMove = 0;
  const pressed = new Map();
  const point = event => {
    const rect = canvas.getBoundingClientRect();
    return { x: (event.clientX - rect.left) / rect.width, y: (event.clientY - rect.top) / rect.height };
  };
  canvas.addEventListener("pointermove", event => {
    const now = performance.now();
    if (now - lastMove < 30) return;
    lastMove = now;
    const position = point(event);
    sendDesktopInput(() => Api.desktopPointer(machineId, position));
  });
  canvas.addEventListener("pointerdown", event => {
    event.preventDefault();
    canvas.focus();
    canvas.setPointerCapture(event.pointerId);
    const position = point(event);
    sendDesktopInput(() => Api.desktopPointer(machineId, { ...position, button: event.button + 1, down: true }));
  });
  canvas.addEventListener("pointerup", event => {
    const position = point(event);
    sendDesktopInput(() => Api.desktopPointer(machineId, { ...position, button: event.button + 1, down: false }));
  });
  canvas.addEventListener("contextmenu", event => event.preventDefault());
  canvas.addEventListener("keydown", event => {
    const keySym = keySymFor(event);
    if (keySym === null) return;
    event.preventDefault();
    if (!pressed.has(event.code)) {
      pressed.set(event.code, keySym);
      sendDesktopInput(() => Api.desktopKey(machineId, { keySym, down: true }));
    }
  });
  canvas.addEventListener("keyup", event => {
    const keySym = pressed.get(event.code);
    if (keySym === undefined) return;
    event.preventDefault();
    pressed.delete(event.code);
    sendDesktopInput(() => Api.desktopKey(machineId, { keySym, down: false }));
  });
  canvas.addEventListener("blur", () => {
    for (const keySym of pressed.values()) {
      sendDesktopInput(() => Api.desktopKey(machineId, { keySym, down: false }));
    }
    pressed.clear();
  });
}

function mountUuDesktop(content) {
  const video = content.querySelector("[data-uu-video]");
  if (!video) return;
  updateDesktopStatus();
  const machineId = state.desktopMachineId;
  const point = event => {
    const rect = video.getBoundingClientRect();
    const width = video.videoWidth || rect.width;
    const height = video.videoHeight || rect.height;
    const scale = Math.min(rect.width / width, rect.height / height);
    const shownWidth = width * scale;
    const shownHeight = height * scale;
    return {
      abs_x: Math.min(1, Math.max(0, (event.clientX - rect.left - (rect.width - shownWidth) / 2) / shownWidth)),
      abs_y: Math.min(1, Math.max(0, (event.clientY - rect.top - (rect.height - shownHeight) / 2) / shownHeight)),
    };
  };
  let lastMove = 0;
  video.addEventListener("pointermove", event => {
    if (performance.now() - lastMove < 30) return;
    lastMove = performance.now();
    window.UUViewer.sendInput({ action: "mouse_move_absolute", ...point(event) });
  });
  video.addEventListener("pointerdown", event => {
    event.preventDefault();
    video.focus();
    video.setPointerCapture(event.pointerId);
    const button = [1, 4, 2, 8, 16][event.button] ?? 1;
    window.UUViewer.sendInput({ action: "mouse_press", button });
  });
  video.addEventListener("pointerup", event => {
    const button = [1, 4, 2, 8, 16][event.button] ?? 1;
    window.UUViewer.sendInput({ action: "mouse_release", button });
  });
  video.addEventListener("contextmenu", event => event.preventDefault());
  video.addEventListener("wheel", event => {
    event.preventDefault();
    window.UUViewer.sendInput({ action: "mouse_scroll", delta_x: Math.round(event.deltaX), delta_y: -Math.round(event.deltaY) });
  }, { passive: false });

  const keyboard = document.createElement("textarea");
  keyboard.setAttribute("aria-label", "UU Remote keyboard input");
  keyboard.autocapitalize = "off";
  keyboard.autocomplete = "off";
  keyboard.spellcheck = false;
  keyboard.style.cssText = "position:fixed;left:-1000px;top:0;width:1px;height:1px;opacity:0";
  content.appendChild(keyboard);
  const pressed = new Map();
  keyboard.addEventListener("input", () => {
    const text = keyboard.value;
    keyboard.value = "";
    if (text) window.UUViewer.sendInput({ action: "text_input", content: text });
  });
  keyboard.addEventListener("keydown", event => {
    if (!event.ctrlKey && !event.metaKey && !event.altKey && event.key.length === 1) return;
    if (event.keyCode === 229 || !event.keyCode) return;
    event.preventDefault();
    if (!pressed.has(event.code)) {
      pressed.set(event.code, event.keyCode);
      window.UUViewer.sendKey(event.keyCode, true);
    }
  });
  keyboard.addEventListener("keyup", event => {
    const key = pressed.get(event.code);
    if (key === undefined) return;
    event.preventDefault();
    pressed.delete(event.code);
    window.UUViewer.sendKey(key, false);
  });
  keyboard.addEventListener("blur", () => {
    for (const key of pressed.values()) window.UUViewer.sendKey(key, false);
    pressed.clear();
  });
  video.addEventListener("pointerdown", () => keyboard.focus());
}

async function refreshMonitor() {
  if (state.page !== "monitor") return;

  const result = await loadMonitorSnapshot();
  if (!result) return;

  state.monitorPhase = result.phase ?? "failed";
  state.monitorError = result.error ?? null;
  state.monitorAttempts = result.attempts ?? [];
  if (result.payload) state.monitor = result.payload;

  // A session that has not produced a reading yet is worth polling faster than the
  // steady-state refresh, so the placeholder turns into data on the first probe
  // rather than after a full interval.
  const waiting = state.monitorPhase === "connecting";

  // Re-render in place: the page is a pure function of the snapshot.
  const content = document.getElementById("app");
  const scroller = content.querySelector(".rx-content");
  const scrollTop = scroller ? scroller.scrollTop : 0;
  render();
  const next = content.querySelector(".rx-content");
  if (next) next.scrollTop = scrollTop;

  if (waiting) setTimeout(refreshMonitor, 400);
}

document.addEventListener("click", async (event) => {
  const nav = event.target.closest("[data-nav]");
  if (nav) {
    event.preventDefault();
    setPage(nav.dataset.nav);
    history.replaceState(null, "", `#${nav.dataset.nav}`);
    return;
  }

  if (event.target.closest("[data-toggle-dark]")) {
    state.dark = !state.dark;
    render();
    return;
  }


  const desktopTarget = event.target.closest("[data-desktop]");
  if (desktopTarget) {
    event.preventDefault();
    openDesktop(desktopTarget.dataset.desktop);
    return;
  }

  if (event.target.closest("[data-vnc-refresh]")) {
    event.preventDefault();
    await loadVncDiscovery(true);
    return;
  }
  const nearbyHost = event.target.closest("[data-vnc-add]");
  if (nearbyHost) {
    event.preventDefault();
    const host = state.vncDiscovery?.hosts.find(item => item.id === nearbyHost.dataset.vncAdd);
    if (host) openSheet("add-server", {
      name: host.displayName || host.name,
      address: host.hostName,
      port: "22",
      vncPort: String(host.port),
      remoteDesktopProtocol: "vnc",
    });
    return;
  }

  if (event.target.closest("[data-desktop-back]")) {
    event.preventDefault();
    history.replaceState(null, "", "#servers");
    loadRoute();
    render();
    return;
  }

  if (event.target.closest("[data-desktop-stop]")) {
    event.preventDefault();
    const id = state.desktopMachineId;
    closeUUViewer();
    const result = await Api.stopDesktop(id);
    state.desktopStartedId = null;
    state.desktopPhase = "idle";
    state.desktopError = result?.error ?? null;
    state.desktopFrameVersion = 0;
    state.desktopSignalVersion = 0;
    state.desktopFrame = null;
    if (result?.error) toast(result.error);
    updateDesktopStatus();
    return;
  }

  if (event.target.closest("[data-terminal-back], [data-files-back]")) {
    event.preventDefault();
    history.replaceState(null, "", "#servers");
    loadRoute();
    render();
    return;
  }
  if (event.target.closest("[data-terminal-stop]")) {
    event.preventDefault();
    const id = state.terminalMachineId;
    closeUUViewer();
    const result = state.terminalTransport === "uu-remote" ? await Api.stopDesktop(id) : await Api.stopTerminal(id);
    state.terminalStartedId = null;
    state.terminalPhase = "idle";
    state.terminalError = result?.error ?? null;
    updateTerminalStatus();
    return;
  }

  if (event.target.closest("[data-files-new-folder]")) {
    event.preventDefault();
    const name = window.prompt("New folder name")?.trim();
    if (!name) return;
    if (name.includes("/") || name.includes("\\")) {
      state.fileError = "A folder name cannot contain a slash.";
      render();
      return;
    }
    const directory = state.filePath.endsWith("/") ? state.filePath : `${state.filePath}/`;
    const path = `${directory}${name}`;
    const result = await Api.createDirectory(state.fileMachineId, path);
    if (!result || result.error) {
      state.fileError = result?.error ?? "Could not create that folder.";
      render();
      return;
    }
    await loadFiles();
    return;
  }
  if (event.target.closest("[data-files-refresh]")) {
    event.preventDefault();
    await loadFiles();
    return;
  }
  if (event.target.closest("[data-files-up]")) {
    event.preventDefault();
    const path = state.filePath.replace(/\/+$/, "") || "/";
    const parent = path === "/" ? "/" : path.slice(0, path.lastIndexOf("/")) || "/";
    state.filePath = parent;
    await loadFiles();
    return;
  }
  const directory = event.target.closest("[data-file-directory]");
  if (directory) {
    event.preventDefault();
    state.filePath = directory.dataset.fileDirectory;
    await loadFiles();
    return;
  }
  const download = event.target.closest("[data-file-download]");
  if (download) {
    event.preventDefault();
    const id = state.fileMachineId;
    const path = download.dataset.fileDownload;
    const result = await Api.downloadFile(id, path);
    if (!result?.blob) { state.fileError = result?.error ?? "Could not download this file."; render(); return; }
    const link = document.createElement("a");
    link.href = URL.createObjectURL(result.blob);
    const disposition = result.disposition ?? "";
    const encodedName = disposition.match(/filename\*=UTF-8''([^;]+)/i)?.[1];
    link.download = encodedName ? decodeURIComponent(encodedName) : path.split("/").pop();
    link.click();
    setTimeout(() => URL.revokeObjectURL(link.href), 1000);
    return;
  }

  // Monitor: start (or reattach to) that server's live session, then show the page.
  const monitorTarget = event.target.closest("[data-monitor]");
  if (monitorTarget) {
    event.preventDefault();
    openMonitor(monitorTarget.dataset.monitor);
    return;
  }

  const connectTarget = event.target.closest("[data-connect]");
  if (connectTarget) {
    event.preventDefault();
    openTerminal(connectTarget.dataset.connect);
    return;
  }

  const filesTarget = event.target.closest("[data-files]");
  if (filesTarget) {
    event.preventDefault();
    openFiles(filesTarget.dataset.files);
    return;
  }

  if (event.target.closest("[data-show-all]")) {
    event.preventDefault();
    setPage("servers");
    return;
  }

  if (event.target.closest("[data-new-server]")) {
    event.preventDefault();
    openSheet("add-server");
    return;
  }

  if (event.target.closest("[data-new-folder]")) {
    event.preventDefault();
    openSheet("add-folder");
    return;
  }

  if (event.target.closest("[data-new-identity]")) {
    event.preventDefault();
    openSheet("add-identity");
    return;
  }

  const moveServer = event.target.closest("[data-move-server]");
  if (moveServer) {
    event.preventDefault();
    state.movingMachineId = moveServer.dataset.moveServer;
    openSheet("move-server");
    return;
  }

  const renameFolder = event.target.closest("[data-rename-folder]");
  if (renameFolder) {
    event.preventDefault();
    const name = renameFolder.dataset.renameFolder;
    const newName = window.prompt("Rename Folder", name);
    if (newName === null) return;
    const result = await Api.renameFolder(name, newName);
    if (result?.error) toast(result.error);
    await loadMachines();
    return;
  }

  const removeFolder = event.target.closest("[data-remove-folder]");
  if (removeFolder) {
    event.preventDefault();
    const name = removeFolder.dataset.removeFolder;
    if (!window.confirm(`Delete "${name}" and move its servers to Ungrouped?`)) return;
    const result = await Api.removeFolder(name);
    if (result?.error) toast(result.error);
    await loadMachines();
    return;
  }

  const removeServer = event.target.closest("[data-remove]");
  if (removeServer) {
    event.preventDefault();
    removeServer(removeServer.dataset.remove);
    return;
  }

  const removeIdentity = event.target.closest("[data-remove-identity]");
  if (removeIdentity) {
    event.preventDefault();
    removeSavedIdentity(removeIdentity.dataset.removeIdentity);
    return;
  }

  if (event.target.closest("[data-close-sheet]")) {
    event.preventDefault();
    closeSheet();
    return;
  }

  // Clicking the scrim (outside the sheet) dismisses it.
  if (event.target.classList?.contains("rx-scrim")) {
    closeSheet();
  }
});

/* ------------------------------------------------------------ quick connect */

document.addEventListener("submit", async (event) => {
  const form = event.target.closest("[data-quick-connect]");
  if (!form) return;
  event.preventDefault();

  const value = new FormData(form).get("target")?.toString().trim() ?? "";
  if (!value) return;

  if (!Api.available) {
    state.quickConnectError = "Quick Connect needs the desktop app.";
    render();
    return;
  }

  // The host parses user@host:port and saves it, then the monitor page opens
  // against whatever it just created.
  const created = await Api.addMachine({ quickConnect: value });
  if (!created || created.error) {
    state.quickConnectError = created?.error ?? "Could not add that server.";
    render();
    return;
  }

  state.quickConnectError = null;
  state.quickConnectValue = "";
  await loadMachines();
  openMonitor(created.id);
});

/* ------------------------------------------------------------ sheets */

function openSheet(kind, vncDraft = null) {
  state.sheet = kind;
  state.sheetError = null;
  state.vncDraft = vncDraft;
  render();
}

/* ------------------------------------------------------------ data loading */

/** Loads the saved servers. Failure is shown, not swallowed. */
async function loadMachines() {
  if (!Api.available) {
    state.machines = null;
    state.folders = null;
    state.folderInfo = null;
    state.machinesError = null;
    return;
  }
  const result = await Api.machines();
  if (!result || result.error) {
    state.machinesError = result?.error ?? "Could not reach the host.";
    state.machines = [];
  } else {
    state.machines = result.machines ?? [];
    if (!state.uuBusy) state.uuRemote = result.uuRemote ?? state.uuRemote;
    state.folders = result.folders ?? [];
    state.folderInfo = result.folderInfo ?? [];
    state.machinesError = result.loadFailed ? result.loadError : null;
  }
  render();
}

async function loadFiles() {
  if (!Api.available || !state.fileMachineId || state.fileBusy || state.machines === null) return;
  const machine = allMachines().find(item => item.id === state.fileMachineId);
  if (!machine) {
    state.fileLoaded = true;
    state.fileError = "No such server.";
    render();
    return;
  }
  if (!state.filePath) state.filePath = machine.fileTransferLoginPath || "/";
  state.fileBusy = true;
  state.fileError = null;
  try {
    const result = await Api.listFiles(state.fileMachineId, state.filePath || "/");
    if (!result || result.error) throw new Error(result?.error ?? "Could not list this folder.");
    state.filePath = result.path ?? state.filePath;
    state.fileEntries = result.entries ?? [];
    state.fileLoaded = true;
  } catch (error) {
    state.fileEntries = [];
    state.fileLoaded = true;
    state.fileError = String(error);
  } finally {
    state.fileBusy = false;
    render();
  }
}

function ensureVncDiscoveryPolling() {
  if (!Api.available || vncDiscoveryTimer !== null) return;
  vncDiscoveryTimer = setInterval(() => void loadVncDiscovery(), 3000);
}

function stopVncDiscoveryPolling() {
  if (vncDiscoveryTimer === null) return;
  clearInterval(vncDiscoveryTimer);
  vncDiscoveryTimer = null;
}

async function loadVncDiscovery(refresh = false) {
  if (!Api.available || state.vncDiscoveryBusy) return;
  state.vncDiscoveryBusy = true;
  updateVncNearby();
  try {
    const result = refresh ? await Api.refreshVncDiscovery() : await Api.vncDiscovery();
    if (!result || result.error) throw new Error(result?.error ?? "Could not browse for nearby VNC hosts.");
    state.vncDiscovery = result;
    state.vncDiscoveryError = result.failure ?? null;
  } catch (error) {
    state.vncDiscoveryError = String(error);
  } finally {
    state.vncDiscoveryBusy = false;
    state.vncDiscoveryLoaded = true;
    updateVncNearby();
  }
}

function updateVncNearby() {
  const current = document.querySelector("[data-vnc-nearby]");
  if (current) current.outerHTML = nearbyVncCard();
}
async function loadIdentities() {
  if (!Api.available) {
    state.identities = null;
    state.identitiesError = null;
    render();
    return;
  }
  const result = await Api.identities();
  if (!result || result.error) {
    state.identitiesError = result?.error ?? "Could not reach the host.";
    state.identities = [];
  } else {
    state.identities = Array.isArray(result) ? result : [];
    state.identitiesError = null;
  }
  render();
}

async function removeServer(id) {
  if (!Api.available) return;
  await Api.removeMachine(id);
  await loadMachines();
}

async function removeSavedIdentity(id) {
  if (!Api.available) return;
  await Api.removeIdentity(id);
  await loadIdentities();
}

/** Opens a server's monitor: starts the session, then shows the page. */
async function openMonitor(machineId) {
  state.monitorMachineId = machineId ?? null;
  state.monitor = null;
  state.monitorError = null;
  state.monitorPhase = "connecting";
  setPage("monitor");
  if (Api.available && machineId) {
    await Api.startMonitor(machineId);
    refreshMonitor();
  } else {
    refreshMonitor();
  }
}

function openDesktop(machineId) {
  state.desktopMachineId = machineId;
  state.desktopStartedId = null;
  state.desktopPhase = "connecting";
  state.desktopError = null;
  state.desktopFrameVersion = 0;
  state.desktopSignalVersion = 0;
  state.desktopFrame = null;
  history.replaceState(null, "", `#desktop/${encodeURIComponent(machineId)}`);
  loadRoute();
  render();
}

function openTerminal(machineId) {
  state.terminalMachineId = machineId;
  state.terminalStartedId = null;
  state.terminalTransport = null;
  state.terminalPhase = "connecting";
  state.terminalError = null;
  state.terminalOutput = "";
  terminalDecoder = new TextDecoder();
  state.desktopSignalVersion = 0;
  history.replaceState(null, "", `#terminal/${encodeURIComponent(machineId)}`);
  loadRoute();
  render();
}

function openFiles(machineId) {
  const machine = allMachines().find(item => item.id === machineId);
  state.fileMachineId = machineId;
  state.filePath = machine?.fileTransferLoginPath || "/";
  state.fileEntries = [];
  state.fileError = null;
  state.fileBusy = false;
  state.fileLoaded = false;
  history.replaceState(null, "", `#files/${encodeURIComponent(machineId)}`);
  loadRoute();
  render();
}

/** A one-line message in the corner; used where a feature is not built yet. */
function toast(message) {
  const existing = document.querySelector(".rx-toast");
  if (existing) existing.remove();
  const node = document.createElement("div");
  node.className = "rx-toast";
  node.textContent = message;
  document.body.appendChild(node);
  setTimeout(() => node.remove(), 2600);
}

/* ------------------------------------------------------------ form submits */

document.addEventListener("submit", async (event) => {
  const terminalForm = event.target.closest("[data-terminal-form]");
  if (terminalForm) {
    event.preventDefault();
    const input = terminalForm.querySelector("[data-terminal-input]");
    const text = input?.value ?? "";
    if (input) input.value = "";
    sendTerminalText(text ? `${text}\r` : "\r");
    return;
  }

  const filesPathForm = event.target.closest("[data-files-path-form]");
  if (filesPathForm) {
    event.preventDefault();
    state.filePath = new FormData(filesPathForm).get("path")?.toString().trim() || "/";
    await loadFiles();
    return;
  }

  const addFolder = event.target.closest("[data-add-folder]");
  if (addFolder) {
    event.preventDefault();
    const name = new FormData(addFolder).get("name")?.toString() ?? "";
    const result = await Api.createFolder(name);
    if (!result || result.error) {
      state.sheetError = result?.error ?? "Could not create that folder.";
      render();
      return;
    }
    closeSheet();
    await loadMachines();
    return;
  }

  const moveServerForm = event.target.closest("[data-move-server-form]");
  if (moveServerForm) {
    event.preventDefault();
    const folder = new FormData(moveServerForm).get("folder")?.toString() ?? "";
    const result = await Api.moveMachine(state.movingMachineId, folder);
    if (!result || result.error) {
      state.sheetError = result?.error ?? "Could not move that server.";
      render();
      return;
    }
    closeSheet();
    await loadMachines();
    return;
  }

  const addServer = event.target.closest("[data-add-server]");
  if (addServer) {
    event.preventDefault();
    const data = new FormData(addServer);
    const result = await Api.addMachine({
      name: data.get("name")?.toString() ?? "",
      address: data.get("address")?.toString() ?? "",
      port: data.get("port")?.toString() || "22",
      group: data.get("group")?.toString() ?? "",
      fileTransferLoginPath: data.get("fileTransferLoginPath")?.toString().trim() || "/",
      comment: data.get("comment")?.toString() ?? "",
      transport: data.get("transport")?.toString() ?? "direct",
      cloudflareServiceTokenId: data.get("cloudflareServiceTokenId")?.toString() ?? "",
      cloudflareServiceTokenSecret: data.get("cloudflareServiceTokenSecret")?.toString() ?? "",
      remoteDesktopProtocol: data.get("remoteDesktopProtocol")?.toString() ?? "none",
      vncPort: data.get("vncPort")?.toString() || "5900",
      vncUsername: data.get("vncUsername")?.toString() ?? "",
      vncPassword: data.get("vncPassword")?.toString() ?? "",
      vncOverSSH: data.get("vncOverSSH") === "on",
      vncShared: data.get("vncShared") === "on",
    });
    if (!result || result.error) {
      state.sheetError = result?.error ?? "Could not save that server.";
      render();
      return;
    }
    closeSheet();
    await loadMachines();
    return;
  }

  const addIdentity = event.target.closest("[data-add-identity]");
  if (addIdentity) {
    event.preventDefault();
    const data = new FormData(addIdentity);
    const username = data.get("username")?.toString().trim() ?? "";
    const password = data.get("password")?.toString() ?? "";
    const privateKey = data.get("privateKey")?.toString() ?? "";
    if (!username) {
      state.sheetError = "A username is required.";
      render();
      return;
    }
    if (!password && !privateKey) {
      state.sheetError = "Give it a password or a private key.";
      render();
      return;
    }
    const result = await Api.addIdentity({
      username,
      password,
      privateKey,
      passphrase: data.get("passphrase")?.toString() ?? "",
      automatic: data.get("automatic") === "on",
    });
    if (!result || result.error) {
      state.sheetError = result?.error ?? "Could not save that identity.";
      render();
      return;
    }
    closeSheet();
    await loadIdentities();
    return;
  }
});

document.addEventListener("change", async event => {
  const picker = event.target.closest("[data-file-upload]");
  if (!picker || !picker.files?.length || !Api.available || !state.fileMachineId) return;
  const files = Array.from(picker.files);
  picker.value = "";
  state.fileBusy = true;
  state.fileError = null;
  render();
  for (const file of files) {
    const directory = state.filePath.endsWith("/") ? state.filePath : `${state.filePath}/`;
    const result = await Api.uploadFile(state.fileMachineId, `${directory}${file.name}`, file);
    if (!result || result.error) {
      state.fileError = result?.error ?? `Could not upload ${file.name}.`;
      break;
    }
  }
  const uploadError = state.fileError;
  state.fileBusy = false;
  state.fileLoaded = false;
  await loadFiles();
  if (uploadError) { state.fileError = uploadError; render(); }
});

function closeSheet() {
  state.sheet = null;
  state.sheetError = null;
  state.movingMachineId = null;
  state.vncDraft = null;
  render();
}

function sheetMarkup() {
  if (!state.sheet) return "";

  const error = state.sheetError
    ? `<div class="rx-help-text rx-help-text--error" style="margin-bottom:var(--space-2)">${escapeHtml(state.sheetError)}</div>`
    : "";

  if (state.sheet === "add-folder") {
    return `<div class="rx-scrim">
      <form class="rx-sheet" data-add-folder>
        <div class="rx-sheet__head">
          <span class="rx-sheet__title">New Folder</span>
          <span class="rx-help-text">Folders keep related servers together.</span>
        </div>
        <div class="rx-sheet__body rx-stack-3">
          ${error}
          <label class="rx-stack-1"><span class="rx-caps-label">Name</span>
            <span class="rx-field"><input name="name" placeholder="Production" required autocomplete="off" /></span></label>
        </div>
        <div class="rx-sheet__footer">
          <button type="button" class="rx-button rx-button--plain" data-close-sheet>Cancel</button>
          <button type="submit" class="rx-button rx-button--primary">Create Folder</button>
        </div>
      </form>
    </div>`;
  }

  if (state.sheet === "move-server") {
    const machine = allMachines().find(item => item.id === state.movingMachineId);
    const current = machine?.group === "Ungrouped" ? "" : machine?.group ?? "";
    const options = ["", ...(state.folders ?? [])].map(name =>
      `<option value="${escapeHtml(name)}"${name === current ? " selected" : ""}>${escapeHtml(name || "Ungrouped")}</option>`
    ).join("");
    return `<div class="rx-scrim">
      <form class="rx-sheet" data-move-server-form>
        <div class="rx-sheet__head">
          <span class="rx-sheet__title">Move Server</span>
          <span class="rx-help-text">${escapeHtml(machine?.name ?? "")}</span>
        </div>
        <div class="rx-sheet__body rx-stack-3">
          ${error}
          <label class="rx-stack-1"><span class="rx-caps-label">Folder</span>
            <span class="rx-field"><select name="folder">${options}</select></span></label>
        </div>
        <div class="rx-sheet__footer">
          <button type="button" class="rx-button rx-button--plain" data-close-sheet>Cancel</button>
          <button type="submit" class="rx-button rx-button--primary">Move</button>
        </div>
      </form>
    </div>`;
  }

  if (state.sheet === "add-server") {
    return `<div class="rx-scrim">
      <form class="rx-sheet" data-add-server>
        <div class="rx-sheet__head">
          <span class="rx-sheet__title">New Server</span>
          <span class="rx-help-text">Give it an address, and a name if you like.</span>
        </div>
        <div class="rx-sheet__body rx-stack-3">
          ${error}
          <label class="rx-stack-1"><span class="rx-caps-label">Name</span>
            <span class="rx-field"><input name="name" value="${escapeHtml(state.vncDraft?.name ?? "")}" placeholder="web-01" autocomplete="off" /></span></label>
          <label class="rx-stack-1"><span class="rx-caps-label">Address</span>
            <span class="rx-field"><input name="address" value="${escapeHtml(state.vncDraft?.address ?? "")}" placeholder="203.0.113.10" required autocomplete="off" /></span></label>
          <label class="rx-stack-1"><span class="rx-caps-label">Transport</span>
            <span class="rx-field"><select name="transport"><option value="direct">Direct (TCP)</option><option value="cloudflare-access">Cloudflare Access</option></select></span></label>
          <label class="rx-stack-1"><span class="rx-caps-label">Cloudflare Access Client ID</span>
            <span class="rx-field"><input name="cloudflareServiceTokenId" autocomplete="off" /></span></label>
          <label class="rx-stack-1"><span class="rx-caps-label">Cloudflare Access Client Secret</span>
            <span class="rx-field"><input name="cloudflareServiceTokenSecret" type="password" autocomplete="new-password" /></span></label>
          <div class="rx-row-3">
            <label class="rx-stack-1" style="width:110px"><span class="rx-caps-label">Port</span>
              <span class="rx-field"><input name="port" value="${escapeHtml(state.vncDraft?.port ?? "22")}" inputmode="numeric" /></span></label>
            <label class="rx-stack-1" style="flex:1"><span class="rx-caps-label">Group</span>
              <span class="rx-field"><input name="group" placeholder="Production" autocomplete="off" /></span></label>
          </div>
          <label class="rx-stack-1"><span class="rx-caps-label">SFTP Start Folder</span>
            <span class="rx-field"><input name="fileTransferLoginPath" value="${escapeHtml(state.vncDraft?.fileTransferLoginPath ?? "/")}" placeholder="/home/user" autocomplete="off" /></span></label>
          <label class="rx-stack-1"><span class="rx-caps-label">Note</span>
            <span class="rx-field"><input name="comment" placeholder="What runs here" autocomplete="off" /></span></label>
          <label class="rx-stack-1"><span class="rx-caps-label">Remote Desktop</span>
            <span class="rx-field"><select name="remoteDesktopProtocol"><option value="none"${state.vncDraft?.remoteDesktopProtocol === "vnc" ? "" : " selected"}>None</option><option value="vnc"${state.vncDraft?.remoteDesktopProtocol === "vnc" ? " selected" : ""}>VNC / Screen Sharing</option></select></span></label>
          <div class="rx-row-3">
            <label class="rx-stack-1" style="width:110px"><span class="rx-caps-label">VNC Port</span>
              <span class="rx-field"><input name="vncPort" value="${escapeHtml(state.vncDraft?.vncPort ?? "5900")}" inputmode="numeric" /></span></label>
            <label class="rx-stack-1" style="flex:1"><span class="rx-caps-label">VNC User Name</span>
              <span class="rx-field"><input name="vncUsername" autocomplete="off" /></span></label>
          </div>
          <label class="rx-stack-1"><span class="rx-caps-label">VNC Password</span>
            <span class="rx-field"><input name="vncPassword" type="password" autocomplete="new-password" /></span></label>
          <label class="rx-row-2" style="gap:8px">
            <input type="checkbox" name="vncOverSSH" />
            <span class="rx-body">Connect VNC through an SSH local forward</span>
          </label>
          <label class="rx-row-2" style="gap:8px">
            <input type="checkbox" name="vncShared" checked />
            <span class="rx-body">Allow other VNC viewers to stay connected</span>
          </label>
        </div>
        <div class="rx-sheet__footer">
          <button type="button" class="rx-button rx-button--plain" data-close-sheet>Cancel</button>
          <button type="submit" class="rx-button rx-button--primary">Add Server</button>
        </div>
      </form>
    </div>`;
  }

  return `<div class="rx-scrim">
      <form class="rx-sheet" data-add-identity>
        <div class="rx-sheet__head">
          <span class="rx-sheet__title">New Identity</span>
          <span class="rx-help-text">A password or a private key. Stored protected by your Windows account.</span>
        </div>
        <div class="rx-sheet__body rx-stack-3">
          ${error}
          <label class="rx-stack-1"><span class="rx-caps-label">Username</span>
            <span class="rx-field"><input name="username" placeholder="root" required autocomplete="off" /></span></label>
          <label class="rx-stack-1"><span class="rx-caps-label">Password</span>
            <span class="rx-field"><input name="password" type="password" autocomplete="new-password" /></span></label>
          <label class="rx-stack-1"><span class="rx-caps-label">Private key</span>
            <span class="rx-field" style="height:auto;padding:8px"><textarea name="privateKey" rows="5" spellcheck="false"
              style="border:0;outline:0;background:transparent;font:var(--font-code);color:var(--rx-ink);resize:vertical;width:100%"
              placeholder="-----BEGIN OPENSSH PRIVATE KEY-----"></textarea></span></label>
          <label class="rx-stack-1"><span class="rx-caps-label">Key passphrase</span>
            <span class="rx-field"><input name="passphrase" type="password" autocomplete="new-password" /></span></label>
          <label class="rx-row-2" style="gap:8px">
            <input type="checkbox" name="automatic" checked />
            <span class="rx-body">Offer this identity to servers set to Automatic</span>
          </label>
        </div>
        <div class="rx-sheet__footer">
          <button type="button" class="rx-button rx-button--plain" data-close-sheet>Cancel</button>
          <button type="submit" class="rx-button rx-button--primary">Add Identity</button>
        </div>
      </form>
    </div>`;
}

window.addEventListener("hashchange", () => {
  loadRoute();
  render();
});

// The .NET shell reads this to keep its title bar in sync with the design's
// own appearance toggle.
window.rxSetDark = (dark) => {
  state.dark = !!dark;
  render();
};

/*
 * `#servers?theme=dark` opens a page with the dark palette already applied, so
 * a screenshot can capture either appearance without clicking anything. The
 * shell passes the same params in location.search (?theme=&bridge=) because the
 * hash is reserved for the page; both feed the same params, hash wins.
 */
function loadRoute() {
  const [route, hashQuery] = (location.hash.slice(1) || "home").split("?");
  const [requestedPage, encodedMachineId] = route.split("/");
  const page = requestedPage;
  state.page = renderers[page] ? page : "home";
  const machineId = encodedMachineId ? decodeURIComponent(encodedMachineId) : null;
  const viewerPurpose = state.page === "desktop" ? "desktop" : state.page === "terminal" ? "terminal" : null;
  const nextViewerKey = viewerPurpose && machineId ? `${machineId}:${viewerPurpose}` : null;
  if (uuViewerKey && uuViewerKey !== nextViewerKey) closeUUViewer();
  if (state.page === "desktop") {
    if (state.desktopMachineId !== machineId) {
      state.desktopMachineId = machineId;
      state.desktopStartedId = null;
      state.desktopPhase = "connecting";
      state.desktopError = null;
      state.desktopFrameVersion = 0;
      state.desktopSignalVersion = 0;
      state.desktopFrame = null;
    }
  } else state.desktopMachineId = null;

  if (state.page === "terminal") {
    if (state.terminalMachineId !== machineId) {
      state.terminalMachineId = machineId;
      state.terminalStartedId = null;
      state.terminalTransport = null;
      state.terminalPhase = "connecting";
      state.terminalError = null;
      state.terminalOutput = "";
      terminalDecoder = new TextDecoder();
      state.terminalPollBusy = false;
      state.desktopSignalVersion = 0;
    }
  } else state.terminalMachineId = null;

  if (state.page === "files") {
    if (state.fileMachineId !== machineId) {
      const machine = state.machines === null ? null : allMachines().find(item => item.id === machineId);
      state.fileMachineId = machineId;
      state.filePath = machine?.fileTransferLoginPath ?? "";
      state.fileEntries = [];
      state.fileError = null;
      state.fileBusy = false;
      state.fileLoaded = false;
    }
  } else state.fileMachineId = null;
  const params = new URLSearchParams(location.search);
  for (const [key, value] of new URLSearchParams(hashQuery || "")) {
    params.set(key, value);
  }
  const theme = params.get("theme");
  if (theme !== null) state.dark = theme === "dark";
}

loadRoute();
render();

// Pull the saved state once at start-up; the pages render their empty states
// while it is in flight, which is what a real client does.
loadMachines();
loadIdentities();
