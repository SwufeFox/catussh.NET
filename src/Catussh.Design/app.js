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
  identities: null,
  identitiesError: null,

  // Monitor page: the session's phase, its last error, the latest reading and
  // the two toggles the Swift cards keep in @State.
  monitor: null,
  monitorError: null,
  monitorPhase: null,
  monitorCores: false,
  monitorSwap: false,
  monitorMachineId: null,

  // A pending dialog: "add-server" | "add-identity" | null.
  sheet: null,
  sheetError: null,
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
  return [...groups.entries()]
    .sort((a, b) => a[0].localeCompare(b[0]))
    .map(([group, items]) => ({ group, items }));
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
          ${borderedButton("Files", icons.files, true, `data-files="${s.id}"`)}
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
        ${borderedButton("Files", "", true, `data-files="${s.id}"`)}
        ${borderedButton("Monitor", "", true, `data-monitor="${s.id}"`)}
        ${borderedButton("Connect", "", true, `data-connect="${s.id}"`)}
      </div>
    </div>`;

  const body = groups
    .map(
      (g) => `
      <div class="rx-table__group">
        <span>${escapeHtml(g.group)}</span><span class="rx-spacer"></span>
        <span class="rx-axis">${g.items.length}</span>
      </div>
      ${g.items.map(row).join("")}`
    )
    .join("");

  return `
    ${toolbar(
      "",
      `${primaryButton("New Server", icons.plus, false, "data-new-server")}
       <label class="rx-field">${icons.search}
         <input placeholder="Search servers" data-search-servers />
       </label>`
    )}
    <div class="rx-page">
      <div class="rx-page-title-row"><h1 class="rx-page-title">Servers</h1></div>
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

function settingsPage() {
  return `
    ${toolbar("", "")}
    <div class="rx-page">
      <div class="rx-page-title-row"><h1 class="rx-page-title">Settings</h1></div>
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

document.addEventListener("click", (event) => {
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

  // Monitor: start (or reattach to) that server's live session, then show the
  // page. This is ServerTool.monitor.show(machine) in the Swift app.
  const monitorTarget = event.target.closest("[data-monitor]");
  if (monitorTarget) {
    event.preventDefault();
    openMonitor(monitorTarget.dataset.monitor);
    return;
  }

  // Connect and Files need the SSH session too; Monitor is the page that exists,
  // so Connect opens it as well rather than pretending to do something.
  const connectTarget = event.target.closest("[data-connect]");
  if (connectTarget) {
    event.preventDefault();
    openMonitor(connectTarget.dataset.connect);
    return;
  }

  const filesTarget = event.target.closest("[data-files]");
  if (filesTarget) {
    event.preventDefault();
    toast("File transfer is not built yet.");
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

  if (event.target.closest("[data-new-identity]")) {
    event.preventDefault();
    openSheet("add-identity");
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

function openSheet(kind) {
  state.sheet = kind;
  state.sheetError = null;
  render();
}

/* ------------------------------------------------------------ data loading */

/** Loads the saved servers. Failure is shown, not swallowed. */
async function loadMachines() {
  if (!Api.available) {
    state.machines = null;
    state.machinesError = null;
    render();
    return;
  }
  const result = await Api.machines();
  if (!result || result.error) {
    state.machinesError = result?.error ?? "Could not reach the host.";
    state.machines = [];
  } else {
    state.machines = result.machines ?? [];
    state.machinesError = result.loadFailed ? result.loadError : null;
  }
  render();
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
  const addServer = event.target.closest("[data-add-server]");
  if (addServer) {
    event.preventDefault();
    const data = new FormData(addServer);
    const result = await Api.addMachine({
      name: data.get("name")?.toString() ?? "",
      address: data.get("address")?.toString() ?? "",
      port: data.get("port")?.toString() || "22",
      group: data.get("group")?.toString() ?? "",
      comment: data.get("comment")?.toString() ?? "",
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

function closeSheet() {
  state.sheet = null;
  state.sheetError = null;
  render();
}

function sheetMarkup() {
  if (!state.sheet) return "";

  const error = state.sheetError
    ? `<div class="rx-help-text rx-help-text--error" style="margin-bottom:var(--space-2)">${escapeHtml(state.sheetError)}</div>`
    : "";

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
            <span class="rx-field"><input name="name" placeholder="web-01" autocomplete="off" /></span></label>
          <label class="rx-stack-1"><span class="rx-caps-label">Address</span>
            <span class="rx-field"><input name="address" placeholder="203.0.113.10" required autocomplete="off" /></span></label>
          <div class="rx-row-3">
            <label class="rx-stack-1" style="width:110px"><span class="rx-caps-label">Port</span>
              <span class="rx-field"><input name="port" value="22" inputmode="numeric" /></span></label>
            <label class="rx-stack-1" style="flex:1"><span class="rx-caps-label">Group</span>
              <span class="rx-field"><input name="group" placeholder="Production" autocomplete="off" /></span></label>
          </div>
          <label class="rx-stack-1"><span class="rx-caps-label">Note</span>
            <span class="rx-field"><input name="comment" placeholder="What runs here" autocomplete="off" /></span></label>
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
  const [page, hashQuery] = (location.hash.slice(1) || "home").split("?");
  state.page = renderers[page] ? page : "home";
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
