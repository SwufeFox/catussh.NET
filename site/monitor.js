/*
 * monitor.js — the Monitor page.
 *
 * Port of MonitorPage / MonitorDashboard / MonitorCards from
 * Foundation/MachineStatusView, plus RXFormat from CatusshDesign/Metric.swift.
 *
 * Data comes from the .NET host at https://catussh.local/monitor-snapshot, where
 * the ported C# parsers turn raw /proc, df, loadavg output into a ServerStatus.
 * Opening the page straight from disk (node tools/serve.mjs) falls back to the
 * same fixture rendered client-side, so the layout can be reviewed without .NET.
 */

/* ------------------------------------------------------------ RXFormat */

const RXFormat = {
  /** `23 %` style percentages: integers. */
  percent(value) {
    if (!Number.isFinite(value)) return "0";
    return String(Math.round(value));
  },
  /** One decimal for rates and sizes. */
  oneDecimal(value) {
    if (!Number.isFinite(value)) return "0";
    return value.toFixed(1);
  },
  /** Splits a byte count into a number and a unit — base 1000, like the Swift. */
  bytes(bytes) {
    const units = ["B", "KB", "MB", "GB", "TB", "PB"];
    let value = Math.max(0, Number.isFinite(bytes) ? bytes : 0);
    let index = 0;
    while (value >= 1000 && index < units.length - 1) {
      value /= 1000;
      index += 1;
    }
    if (index === 0 || value >= 100) return [String(Math.round(value)), units[index]];
    return [value.toFixed(1), units[index]];
  },
  bytesString(value) {
    const [number, unit] = RXFormat.bytes(value);
    return `${number} ${unit}`;
  },
  rate(bytesPerSecond) {
    const [number, unit] = RXFormat.bytes(bytesPerSecond);
    return [number, `${unit}/s`];
  },
  rateString(bytesPerSecond) {
    const [number, unit] = RXFormat.rate(bytesPerSecond);
    return `${number} ${unit}`;
  },
  /** "41 days, 3 h" style uptime; two largest units. */
  duration(seconds) {
    if (!Number.isFinite(seconds) || seconds <= 0) return "—";
    const total = Math.floor(seconds);
    const days = Math.floor(total / 86400);
    const hours = Math.floor((total % 86400) / 3600);
    const minutes = Math.floor((total % 3600) / 60);
    const parts = [];
    if (days > 0) parts.push(`${days} d`);
    if (hours > 0) parts.push(`${hours} h`);
    if (parts.length < 2 && minutes > 0) parts.push(`${minutes} min`);
    if (parts.length === 0) parts.push("0 min");
    return parts.join(" ");
  },
};

/* --------------------------------------------------------- small components */

const metric = (value, unit = "") => `
  <div class="rx-metric">
    <span class="rx-metric__value">${value}</span>
    ${unit ? `<span class="rx-metric__unit">${unit}</span>` : ""}
  </div>`;

const labelMetric = ({ label, value, unit = "", swatch = null }) => `
  <div class="rx-label-metric">
    <div class="rx-label-metric__label">
      ${swatch ? `<span class="rx-label-metric__swatch" style="background:${swatch}"></span>` : ""}
      <span class="rx-caps-label">${label}</span>
    </div>
    <div class="rx-label-metric__value">
      <span class="rx-metric__value">${value}</span>
      ${unit ? `<span class="rx-metric__unit">${unit}</span>` : ""}
    </div>
  </div>`;

const subMetricRow = (items) =>
  `<div class="rx-submetrics">${items.map(labelMetric).join("")}</div>`;

/** UsageBar: 6pt track, one or more segments, 2pt apart. */
const usageBar = (segments, height = 6) => `
  <div class="rx-usage-bar" style="height:${height}px">
    ${segments
      .map(([label, fraction, color]) => {
        const clamped = Math.max(0, Math.min(1, Number.isFinite(fraction) ? fraction : 0));
        return `<span title="${label}" style="width:${clamped * 100}%;background:${color}"></span>`;
      })
      .join("")}
  </div>`;

/** SplitBar: 20pt segmented bar with 4pt gaps. */
const splitBar = (parts) => {
  const total = Math.max(parts.reduce((sum, [value]) => sum + Math.max(0, value), 0), 0.0001);
  return `<div class="rx-split-bar">
    ${parts
      .map(([value, color]) => {
        const share = (Math.max(0, value) / total) * 100;
        return `<span style="width:calc(${share}% - 4px);background:${color}"></span>`;
      })
      .join("")}
  </div>`;
};

/**
 * Usage color for disks and VRAM: warning at 75%, danger at 90% — the
 * rxUsageColor rule from Charts.swift.
 */
const usageColor = (percent, base = "var(--rx-accent)") => {
  if (percent >= 90) return "var(--rx-danger)";
  if (percent >= 75) return "var(--rx-warning)";
  return base;
};

/** UsageListRow: monospaced name, bold value, detail, bar underneath. */
const usageListRow = ({ name, value, detail = "", bar }) => `
  <div class="rx-usage-row">
    <div class="rx-usage-row__head">
      <span class="rx-usage-row__name">${escapeHtml(name)}</span>
      <span class="rx-spacer"></span>
      <span class="rx-usage-row__value">${value}</span>
      ${detail ? `<span class="rx-usage-row__detail">· ${detail}</span>` : ""}
    </div>
    ${bar}
  </div>`;

const escapeHtml = (text) =>
  String(text).replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));

/**
 * Sparkline: filled area + stroked line. The Swift draws it with a Path over
 * the sample list; here the same polyline is scaled into a viewBox.
 */
function sparkline(values, color, fill) {
  if (!values || values.length < 2) {
    return `<svg class="rx-sparkline" viewBox="0 0 100 40" preserveAspectRatio="none"></svg>`;
  }
  const max = Math.max(...values, 1);
  const step = 100 / Math.max(values.length - 1, 1);
  const points = values.map((value, index) => {
    const x = index * step;
    const y = 40 - Math.max(0, Math.min(1, value / max)) * 40;
    return [x, y];
  });
  const line = points.map(([x, y]) => `${x.toFixed(2)},${y.toFixed(2)}`).join(" ");
  const area = `0,40 ${line} 100,40`;
  return `<svg class="rx-sparkline" viewBox="0 0 100 40" preserveAspectRatio="none">
    <polygon points="${area}" fill="${fill}"></polygon>
    <polyline points="${line}" fill="none" stroke="${color}" stroke-width="1.5"
      stroke-linejoin="round" stroke-linecap="round" vector-effect="non-scaling-stroke"></polyline>
  </svg>`;
}

/* ----------------------------------------------------------------- cards */

function systemCard(status) {
  const s = status.system;
  return `<section class="rx-card rx-rise" style="animation-delay:0ms">
    <div class="rx-card-head"><span class="rx-caps-label">Load</span></div>
    ${metric(RXFormat.oneDecimal(s.load1), "1 min")}
    ${subMetricRow([
      { label: "5 min", value: RXFormat.oneDecimal(s.load5) },
      { label: "15 min", value: RXFormat.oneDecimal(s.load15) },
      { label: "Processes", value: String(s.runningProcs), unit: `/ ${s.totalProcs} running` },
    ])}
  </section>`;
}

function processorCard(status, state, index) {
  const processor = status.processor;
  const summary = processor.summary;
  const count = processor.cores.length;
  const title = count > 0 ? `Processor · ${count} ${count === 1 ? "core" : "cores"}` : "Processor";
  const showCores = state.monitorCores;

  const segmented = `<div class="rx-segmented" data-monitor-segmented="cores">
    <button aria-selected="${!showCores}" data-cores="false">Summary</button>
    <button aria-selected="${showCores}" data-cores="true">Cores</button>
  </div>`;

  const body = showCores
    ? coreGrid(processor.cores)
    : `${usageBar([
        ["User", summary.sumUser / 100, "var(--rx-accent)"],
        ["System", summary.sumSystem / 100, "var(--rx-series-2)"],
        ["IO wait", summary.sumIOWait / 100, "var(--rx-warning)"],
        ["Nice", summary.sumNice / 100, "var(--rx-control-border)"],
      ])}
      <div style="height:var(--space-3)"></div>
      ${subMetricRow([
        { label: "User", value: RXFormat.oneDecimal(summary.sumUser), unit: "%", swatch: "var(--rx-accent)" },
        { label: "System", value: RXFormat.oneDecimal(summary.sumSystem), unit: "%", swatch: "var(--rx-series-2)" },
        { label: "IO wait", value: RXFormat.oneDecimal(summary.sumIOWait), unit: "%", swatch: "var(--rx-warning)" },
        { label: "Nice", value: RXFormat.oneDecimal(summary.sumNice), unit: "%", swatch: "var(--rx-control-border)" },
      ]).replace('class="rx-submetrics"', 'class="rx-submetrics" style="margin-top:var(--space-4)"')}`;

  return `<section class="rx-card rx-rise" style="animation-delay:${index * 40}ms">
    <div class="rx-card-head">
      <span class="rx-caps-label">${title}</span>
      <span class="rx-spacer"></span>
      ${segmented}
    </div>
    ${metric(RXFormat.oneDecimal(summary.sumUsed), "%")}
    <div style="margin-top:var(--space-2)">${body}</div>
  </section>`;
}

/** Per-core tiles; eight per row, sixteen for large core counts. */
function coreGrid(cores) {
  if (!cores || cores.length === 0) return `<div class="rx-help-text" style="margin-top:var(--space-2)">No data</div>`;
  const dense = cores.length > 64;
  return `<div class="rx-core-grid${dense ? " rx-core-grid--dense" : ""}">
    ${cores
      .map((core, index) => {
        const percent = Math.max(0, Math.min(100, core.sumUsed));
        return `<div class="rx-core-tile rx-core-tile--grid" title="${escapeHtml(core.name || `core ${index}`)} · ${RXFormat.oneDecimal(percent)}%">
          <div class="rx-core-tile__fill" style="height:${percent}%"></div>
          <span class="rx-core-tile__name">${index}</span>
          <span class="rx-core-tile__value">${RXFormat.percent(percent)}%</span>
        </div>`;
      })
      .join("")}
  </div>`;
}

function memoryCard(status, totals, state, index) {
  const memory = status.memory;
  const total = memory.memTotal;
  const showSwap = state.monitorSwap;
  const heading = total > 0 ? `Memory · ${RXFormat.bytesString(total * 1024)}` : "Memory";

  const segmented = `<div class="rx-segmented">
    <button aria-selected="${!showSwap}" data-swap="false">RAM</button>
    <button aria-selected="${showSwap}" data-swap="true">Swap</button>
  </div>`;

  const body = showSwap
    ? (() => {
        const swapTotal = memory.swapTotal;
        const swapUsed = Math.max(0, swapTotal - memory.swapFree);
        return `
          ${metric(RXFormat.percent(swapTotal > 0 ? (swapUsed / swapTotal) * 100 : 0), "%")}
          <div style="margin-top:var(--space-3)">
            ${splitBar([
              [swapUsed, "var(--rx-accent)"],
              [Math.max(0, swapTotal - swapUsed), "var(--rx-surface-sunken)"],
            ])}
          </div>
          <div style="display:flex;gap:var(--space-4);margin-top:var(--space-3)">
            ${legendItem("Used", RXFormat.bytesString(swapUsed * 1024), "var(--rx-accent)")}
            ${legendItem("Free", RXFormat.bytesString(memory.swapFree * 1024), "var(--rx-surface-sunken)", true)}
          </div>
          ${swapTotal <= 0 ? `<div class="rx-help-text" style="margin-top:var(--space-3)">No swap</div>` : ""}`;
      })()
    : `
      ${metric(RXFormat.oneDecimal(totals.memoryUsedPercent), "%")}
      <div style="margin-top:var(--space-3)">
        ${splitBar([
          [totals.memoryUsedKB, "var(--rx-accent)"],
          [totals.memoryCacheKB, "var(--rx-series-2)"],
          [memory.memFree, "var(--rx-surface-sunken)"],
        ])}
      </div>
      <div style="display:flex;flex-wrap:wrap;gap:var(--space-4);margin-top:var(--space-3)">
        ${legendItem("Used", RXFormat.bytesString(totals.memoryUsedKB * 1024), "var(--rx-accent)")}
        ${legendItem("Cache", RXFormat.bytesString(totals.memoryCacheKB * 1024), "var(--rx-series-2)")}
        ${legendItem("Free", RXFormat.bytesString(memory.memFree * 1024), "var(--rx-surface-sunken)", true)}
      </div>`;

  return `<section class="rx-card rx-rise" style="animation-delay:${index * 40}ms">
    <div class="rx-card-head">
      <span class="rx-caps-label">${heading}</span>
      <span class="rx-spacer"></span>
      ${segmented}
    </div>
    <div style="margin-top:var(--space-1)">${body}</div>
  </section>`;
}

function legendItem(label, value, color, outlined = false) {
  return `<div class="rx-legend-item">
    <span class="rx-legend-item__swatch" style="background:${color}${
      outlined ? ";box-shadow:inset 0 0 0 1px var(--rx-hairline)" : ""
    }"></span>
    <span>${label}</span>
    <span class="rx-legend-item__value">${value}</span>
  </div>`;
}

function diskCard(status, index) {
  const elements = status.fileSystem.elements;
  return `<section class="rx-card rx-rise" style="animation-delay:${index * 40}ms">
    <div class="rx-card-head"><span class="rx-caps-label">Disks</span></div>
    ${
      elements.length === 0
        ? `<div class="rx-help-text" style="margin-top:var(--space-2)">No data</div>`
        : `<div class="rx-usage-row__list" style="margin-top:var(--space-2)">
            ${elements
              .map((disk) =>
                usageListRow({
                  name: disk.mountPoint,
                  value: `${RXFormat.percent(disk.percent)}%`,
                  detail: `${disk.used} of ${disk.size}`,
                  bar: usageBar([["used", disk.percent / 100, usageColor(disk.percent)]]),
                }),
              )
              .join("")}
          </div>`
    }
  </section>`;
}

function throughputCard(status, totals, history, direction, index) {
  const isReceive = direction === "receive";
  const series = isReceive ? history.receive : history.transmit;
  const current = isReceive ? totals.receivePerSecond : totals.transmitPerSecond;
  const peak = series.length ? Math.max(...series) : 0;
  const [value, unit] = RXFormat.rate(current);
  const color = isReceive ? "var(--rx-series-2)" : "var(--rx-accent)";
  const fill = isReceive ? "var(--rx-series-2-fill)" : "var(--rx-accent-fill)";

  return `<section class="rx-card rx-card--throughput rx-rise" style="animation-delay:${index * 40}ms">
    <div class="rx-card-head">
      <span class="rx-caps-label">${isReceive ? "Receive" : "Transmit"}</span>
      <span class="rx-spacer"></span>
      ${peak > 0 ? `<span class="rx-hint-text">peak ${RXFormat.rateString(peak)}</span>` : ""}
    </div>
    ${metric(value, unit)}
    <div style="margin-top:auto">${sparkline(series, color, fill)}</div>
  </section>`;
}

function networkCard(status, index) {
  const elements = [...status.network.elements].sort(
    (a, b) => b.rxBytesPerSec + b.txBytesPerSec - (a.rxBytesPerSec + a.txBytesPerSec),
  );
  const peak = Math.max(
    1,
    ...elements.map((e) => Math.max(e.rxBytesPerSec, e.txBytesPerSec)),
  );

  return `<section class="rx-card rx-rise" style="animation-delay:${index * 40}ms">
    <div class="rx-card-head"><span class="rx-caps-label">Network</span></div>
    ${
      elements.length === 0
        ? `<div class="rx-help-text" style="margin-top:var(--space-2)">No data</div>`
        : `<div class="rx-usage-row__list" style="margin-top:var(--space-2)">
            ${elements
              .map((element) =>
                usageListRow({
                  name: element.device,
                  value: `↓ ${RXFormat.rateString(element.rxBytesPerSec)}`,
                  detail: `↑ ${RXFormat.rateString(element.txBytesPerSec)}`,
                  bar: usageBar([
                    ["Receive", element.rxBytesPerSec / peak / 2, "var(--rx-series-2)"],
                    ["Transmit", element.txBytesPerSec / peak / 2, "var(--rx-accent)"],
                  ]),
                }),
              )
              .join("")}
          </div>`
    }
  </section>`;
}

function graphicsCard(status, index) {
  const graphics = status.graphics;
  if (!graphics || !graphics.units || graphics.units.length === 0) return "";
  const count = graphics.units.length;
  return `<section class="rx-card rx-rise" style="animation-delay:${index * 40}ms">
    <div class="rx-card-head">
      <span class="rx-caps-label">Graphics · ${count} ${count === 1 ? "GPU" : "GPUs"}</span>
      <span class="rx-spacer"></span>
      <span class="rx-hint-text">Driver ${escapeHtml(graphics.version)}</span>
    </div>
    <div style="display:flex;flex-direction:column;gap:var(--space-4);margin-top:var(--space-2)">
      ${graphics.units.map(gpuRow).join("")}
    </div>
  </section>`;
}

function gpuRow(unit) {
  const utilisation = Number(unit.utilization.gpu_util) || 0;
  const vram = unit.memory.total > 0 ? (unit.memory.used / unit.memory.total) * 100 : 0;
  const bar = (title, percent, color) => `
    <div class="rx-gpu-bar">
      <span class="rx-caps-label">${title}</span>
      ${usageBar([["value", percent / 100, usageColor(percent, color)]])}
      <span class="rx-gpu-bar__value">${RXFormat.percent(percent)}%</span>
    </div>`;
  return `<div class="rx-gpu-row">
    <div class="rx-gpu-row__head">
      <span class="rx-body-strong">${escapeHtml(unit.name)}</span>
      <span class="rx-spacer"></span>
      <span class="rx-gpu-row__fan">Fan ${escapeHtml(unit.fan_speed)}</span>
    </div>
    ${bar("GPU", utilisation, "var(--rx-accent)")}
    ${bar("VRAM", vram, "var(--rx-series-2)")}
    ${unit.vbios_version ? `<span class="rx-hint-text">vbios ${escapeHtml(unit.vbios_version)}</span>` : ""}
  </div>`;
}

/* ---------------------------------------------------------------- page */

function monitorFacts(status) {
  const system = status.system;
  const facts = [
    ["Hostname", system.hostname || "—", false],
    ["OS", system.releaseName || "—", false],
    ["Uptime", RXFormat.duration(system.uptimeSec), false],
    ["Address", status.address || "—", true],
  ];
  return `<div class="rx-facts">
    ${facts
      .map(
        ([label, value, mono]) => `
      <div class="rx-fact">
        <span class="rx-caps-label">${label}</span>
        <span class="rx-fact__value${mono ? " rx-fact__value--mono" : ""}">${escapeHtml(value)}</span>
      </div>`,
      )
      .join("")}
  </div>`;
}

function monitorDashboard(status, totals, history, state) {
  return `<div class="rx-monitor">
    <div class="rx-monitor__column">
      ${systemCard(status)}
      ${processorCard(status, state, 2)}
      ${memoryCard(status, totals, state, 4)}
      ${diskCard(status, 6)}
    </div>
    <div class="rx-monitor__column">
      <div style="display:flex;gap:var(--space-4)">
        ${throughputCard(status, totals, history, "receive", 1)}
        ${throughputCard(status, totals, history, "transmit", 1)}
      </div>
      ${networkCard(status, 3)}
      ${graphicsCard(status, 5)}
    </div>
  </div>`;
}

/**
 * The page body. `status` is the host payload ({ status, totals, history }) or
 * null while the first reading is on its way.
 */
function monitorPage(state) {
  const payload = state.monitor;
  const phase = state.monitorPhase ?? (payload ? "connected" : "connecting");

  const title = payload
    ? payload.status.system.hostname || "Server"
    : machineName(state.monitorMachineId) || "Server";

  // The phase the original only sketched: connecting, failed (with the reason and
  // what was tried), lost (stale data still on screen), and the demo reading.
  const badge = {
    connected: "",
    connecting: `<span class="rx-tag">connecting…</span>`,
    demo: `<span class="rx-tag">demo data</span>`,
    lost: `<span class="rx-tag rx-tag--warning">connection lost</span>`,
    failed: `<span class="rx-tag rx-tag--warning">can't connect</span>`,
    closed: `<span class="rx-tag">closed</span>`,
  }[phase] ?? "";

  const header = payload
    ? `<div class="rx-stack-4" style="margin-bottom:var(--space-5)">
         <div class="rx-row-3"><h1 class="rx-page-title">${escapeHtml(title)}</h1>${badge}</div>
         ${monitorFacts(payload.status)}
       </div>`
    : `<div class="rx-row-3" style="margin-bottom:var(--space-5)">
         <h1 class="rx-page-title">${escapeHtml(title)}</h1>${badge}
       </div>`;

  const body = payload
    ? `${phase === "lost"
        ? `<div class="rx-help-text rx-help-text--error" style="margin-bottom:var(--space-3)">
             ${escapeHtml(state.monitorError || "The connection dropped. Showing the last reading.")}
           </div>`
        : ""}
       ${monitorDashboard(payload.status, payload.totals, payload.history, state)}`
    : placeholder(state, phase);

  return `${toolbar(
    "",
    `<button class="rx-button rx-button--plain" data-monitor-action="menubar">Show in Menu Bar</button>
     <button class="rx-button rx-button--plain" data-monitor-action="close">Close Monitor</button>`,
  )}
  <div class="rx-page" style="padding-top:0">
    ${header}
    ${body}
  </div>`;
}

/** The saved server's name, for the title before the first reading arrives. */
function machineName(id) {
  if (!id) return null;
  for (const group of state.machines ?? []) {
    for (const item of group.items) if (item.id === id) return item.name;
  }
  return null;
}

/**
 * What the page shows before there is a reading. The original left this as one
 * generic card; a real client needs to say which failure it is, and what to do.
 */
function placeholder(state, phase) {
  if (phase === "failed") {
    const attempts = state.monitorAttempts ?? [];
    const tried = attempts.length > 1
      ? `<div class="rx-stack-1" style="margin-top:var(--space-3);text-align:left">
           <span class="rx-caps-label">Tried</span>
           ${attempts
             .map(
               (a) => `<div class="rx-row-2 rx-help-text">
                 <span>${escapeHtml(a.credential)}</span>
                 <span class="rx-spacer"></span>
                 <span class="${a.succeeded ? "" : "rx-help-text--error"}">${a.succeeded ? "ok" : escapeHtml(a.error || "refused")}</span>
               </div>`
             )
             .join("")}
         </div>`
      : "";

    return `<div class="rx-card rx-monitor-placeholder">
      <span class="rx-section-title">Can't connect</span>
      <span class="rx-help-text">${escapeHtml(state.monitorError || "The server did not answer.")}</span>
      ${tried}
      <div class="rx-row-2" style="margin-top:var(--space-4)">
        <button class="rx-button rx-button--primary rx-button--small" data-monitor-retry>Try Again</button>
        <button class="rx-button rx-button--plain" data-monitor-action="identities">Add Identity</button>
      </div>
    </div>`;
  }

  if (phase === "closed") {
    return `<div class="rx-card rx-monitor-placeholder">
      <span class="rx-section-title">Monitor closed</span>
      <span class="rx-help-text">Reopen it from the server's Monitor button.</span>
    </div>`;
  }

  return `<div class="rx-card rx-monitor-placeholder">
    <div class="rx-spinner"></div>
    <span>Connecting…</span>
  </div>`;
}

/** Wires the page's toggles; called after each render. */
function mountMonitor(root) {
  root.querySelectorAll("[data-cores]").forEach((button) => {
    button.addEventListener("click", () => {
      state.monitorCores = button.dataset.cores === "true";
      render();
    });
  });
  root.querySelectorAll("[data-swap]").forEach((button) => {
    button.addEventListener("click", () => {
      state.monitorSwap = button.dataset.swap === "true";
      render();
    });
  });
  root.querySelectorAll("[data-monitor-action]").forEach((button) => {
    button.addEventListener("click", () => {
      const action = button.dataset.monitorAction;
      if (action === "close") setPage("servers");
      if (action === "identities") setPage("identities");
    });
  });

  // Retry re-asks the host to (re)start the session for this server.
  root.querySelectorAll("[data-monitor-retry]").forEach((button) => {
    button.addEventListener("click", () => {
      if (state.monitorMachineId) openMonitor(state.monitorMachineId);
    });
  });
}

/**
 * Fetches one reading from the host. The .NET shell passes its loopback bridge
 * port as ?bridge=; without it (file:// or the plain static server) the page
 * falls back to the baked fixture, and to null when even that is missing.
 */
async function loadMonitorSnapshot() {
  // A real server: the host runs the SSH probe for this machine and reports the
  // session's phase alongside the reading, so the page can tell "connecting" from
  // "authentication failed" from "lost the connection".
  if (state.monitorMachineId && Api.available) {
    const session = await Api.monitor(state.monitorMachineId);
    if (!session || session.error) {
      return { phase: "failed", error: session?.error ?? "Could not reach the host.", payload: null };
    }
    return {
      phase: session.phase,
      error: session.error,
      attempts: session.attempts ?? [],
      payload: session.payload,
    };
  }

  // No saved server (or no host): the built-in demo reading, so the layout still
  // has something to show. It is labelled as demo in the placeholder header.
  if (Api.available) {
    const demo = await Api.demoMonitor();
    if (demo && !demo.error) return { phase: "demo", error: null, payload: demo };
  }

  try {
    const response = await fetch("monitor-fixture.json", { cache: "no-store" });
    if (!response.ok) return { phase: "failed", error: "No reading available.", payload: null };
    return { phase: "demo", error: null, payload: await response.json() };
  } catch {
    return { phase: "failed", error: "No reading available.", payload: null };
  }
}
