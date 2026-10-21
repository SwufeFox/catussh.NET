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
  { id: "servers", label: "Servers", icon: "servers", count: servers.length },
  { id: "identities", label: "Identities", icon: "identities", count: 1 },
  { id: "snippets", label: "Snippets", icon: "snippets" },
  { id: "portforward", label: "Port Forward", icon: "portforward" },
];

const state = { page: "home", dark: false };

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
          return `${separator}
          <a class="rx-sidebar__row${selected ? " rx-sidebar__row--selected" : ""}"
             href="#${p.id}" data-nav="${p.id}">
            ${icons[p.icon]}
            <span>${p.label}</span>
            ${p.count ? `<span class="rx-sidebar__count">${p.count}</span>` : ""}
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

const primaryButton = (label, icon = icons.plus, small = false) =>
  `<button class="rx-button rx-button--primary${small ? " rx-button--small" : ""}">${icon}${label}</button>`;

const borderedButton = (label, icon = "", small = false) =>
  `<button class="rx-button rx-button--bordered${small ? " rx-button--small" : ""}">${icon}${label}</button>`;

const plainButton = (label, icon = "") =>
  `<button class="rx-button rx-button--plain${icon ? "" : ""}">${icon}${label}</button>`;

const iconButton = (icon) => `<button class="rx-icon-button" aria-label="More">${icon}</button>`;

/* ---------------------------------------------------------------- pages */

function homePage() {
  const cards = homeServers
    .map(
      (s) => `
      <div class="rx-card rx-server-card">
        <div>
          <div class="rx-server-card__name">${s.name}</div>
          <div class="rx-server-card__address">${s.address}</div>
        </div>
        <div class="rx-server-card__meta">
          <div class="rx-server-card__meta-row">
            <span class="rx-caps-label">Last used</span>
            <span class="rx-spacer"></span>
            <span class="rx-server-card__meta-value">${s.lastUsed}</span>
          </div>
          <div class="rx-server-card__meta-row">
            <span class="rx-caps-label">Identity</span>
            <span class="rx-spacer"></span>
            <span class="rx-server-card__meta-value">Automatic</span>
          </div>
        </div>
        <div class="rx-server-card__actions">
          ${primaryButton("Connect", icons.terminal, true)}
          ${borderedButton("Files", icons.files, true)}
          ${borderedButton("Monitor", icons.monitor, true)}
          ${iconButton(icons.more)}
        </div>
      </div>`
    )
    .join("");

  return `
    ${toolbar(`<span class="rx-spacer"></span>`, primaryButton("New Server"))}
    <div class="rx-page" style="padding-top: 0;">
      <label class="rx-home-search">
        ${icons.terminal}
        <input placeholder="user@host:22" spellcheck="false" />
        <button class="rx-icon-button" style="opacity:.45">${icons.plus}</button>
      </label>
      <div class="rx-section-title-row">
        <span class="rx-section-title">Servers</span>
        <span class="rx-hint-text">${homeServers.length}</span>
        <span class="rx-spacer"></span>
        ${plainButton("Show All")}
      </div>
      <div class="rx-server-grid">
        ${cards}
        <button class="rx-new-card">${icons.plus}<span>New Server</span></button>
      </div>
    </div>`;
}

function serversPage() {
  const groups = [
    { name: "CI", items: servers.filter((s) => s.group === "CI") },
    { name: "Home Lab", items: servers.filter((s) => s.group === "Home Lab") },
    { name: "Production", items: servers.filter((s) => s.group === "Production") },
  ];

  const row = (s) => `
    <div class="rx-table__row rx-table__row--body" data-server="${s.name}">
      <div class="rx-table__cell rx-table__cell--name">
        <div class="rx-body">${s.name}</div>
        <div class="rx-table__sub">${s.note}</div>
      </div>
      <div class="rx-table__cell rx-table__cell--address rx-table__mono rx-body">${s.address}</div>
      <div class="rx-table__cell rx-table__cell--identity rx-body"
           style="color:var(--rx-ink-secondary)">${s.identity}</div>
      <div class="rx-table__cell rx-table__cell--cpu rx-table__cpu">
        <span class="rx-body rx-table__mono" style="color:var(--rx-ink-secondary)">${s.cpu}</span>
      </div>
      <div class="rx-table__cell rx-table__cell--last-used rx-body"
           style="color:var(--rx-ink-secondary)">${s.lastUsed}</div>
      <div class="rx-table__cell rx-table__cell--actions">
        ${borderedButton("Files", "", true)}
        ${borderedButton("Monitor", "", true)}
        ${borderedButton("Connect", "", true)}
      </div>
    </div>`;

  const body = groups
    .map(
      (g) => `
      <div class="rx-table__group">
        <span>${g.name}</span><span class="rx-spacer"></span>
        <span class="rx-axis">${g.items.length}</span>
      </div>
      ${g.items.map(row).join("")}`
    )
    .join("");

  return `
    ${toolbar(
      "",
      `<div class="rx-toolbar__pill">${icons.eye}<span>Privacy</span>
         <span class="rx-toolbar__divider"></span>
         ${icons.windows}<span>Open Terminals…</span>
       </div>
       ${primaryButton("New Server")}
       <label class="rx-field">${icons.search}
         <input placeholder="Search servers" />
       </label>`
    )}
    <div class="rx-page">
      <div class="rx-page-title-row"><h1 class="rx-page-title">Servers</h1></div>
      <div class="rx-card" style="padding: var(--space-2);">
        <div class="rx-table">
          <div class="rx-table__row rx-table__row--head rx-table__row--body">
            <div class="rx-table__cell rx-table__cell--name">Name</div>
            <div class="rx-table__cell rx-table__cell--address">Address</div>
            <div class="rx-table__cell rx-table__cell--identity">Identity</div>
            <div class="rx-table__cell rx-table__cell--cpu">CPU</div>
            <div class="rx-table__cell rx-table__cell--last-used">Last used</div>
            <div class="rx-table__cell rx-table__cell--actions"></div>
          </div>
          ${body}
        </div>
      </div>
    </div>`;
}

function identitiesPage() {
  return `
    ${toolbar("", primaryButton("New Identity"))}
    <div class="rx-page">
      <div class="rx-page-title-row"><h1 class="rx-page-title">Identities</h1></div>
      <div class="rx-card rx-card--chrome">
        <div class="rx-divided-stack">
          <div style="display:flex;align-items:center;gap:12px;height:56px;padding:0 16px;">
            <span class="rx-symbol-tile">${icons.identities}</span>
            <div style="display:flex;flex-direction:column;gap:2px;">
              <span class="rx-body">ignored</span>
              <span class="rx-help" style="color:var(--rx-ink-tertiary)">Automatic · password</span>
            </div>
            <span class="rx-spacer"></span>
            <span class="rx-tag">1 server</span>
            ${plainButton("Edit")}
          </div>
        </div>
      </div>
      <div class="rx-help-text" style="text-align:center;color:var(--rx-ink-tertiary)">
        Identities (passwords or keys) are shared across servers.
      </div>
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

const renderers = {
  home: homePage,
  servers: serversPage,
  identities: identitiesPage,
  snippets: snippetsPage,
  portforward: portForwardPage,
  settings: settingsPage,
};

/* ---------------------------------------------------------------- router */

function render() {
  document.documentElement.classList.toggle("dark", state.dark);
  const page = renderers[state.page] || homePage;
  document.getElementById("app").innerHTML = `
    <div class="rx-window">
      ${sidebar()}
      <main class="rx-content">${page()}</main>
    </div>`;
}

function setPage(id) {
  state.page = id;
  render();
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
  }
});

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
 * shell uses window.rxSetDark() instead.
 */
function loadRoute() {
  const [page, query] = (location.hash.slice(1) || "home").split("?");
  state.page = renderers[page] ? page : "home";
  for (const [key, value] of new URLSearchParams(query || "")) {
    if (key === "theme") state.dark = value === "dark";
  }
}

loadRoute();
render();
