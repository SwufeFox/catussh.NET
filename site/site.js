/*
 * site.js — the product page's behaviour.
 *
 * The hero is not a screenshot: it calls the app's own card functions from
 * monitor.js and renders them with the app's own CSS, against a reading produced
 * by the real parsers (monitor-fixture.json, written by tools/gen-monitor-fixture.mjs).
 * So if a card's markup changes in the app, this page changes with it.
 */

/* monitor.js reads a global `state` for its card toggles. */
var state = { monitorCores: false, monitorSwap: false };

/* monitor.js's snapshot loader looks for a host; this page has none. */
var Api = { available: false };

/* This page escapes its own strings rather than reaching into monitor.js. */
function esc(text) {
  return String(text).replace(/[&<>"']/g, (c) =>
    ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]),
  );
}

/* ------------------------------------------------------------ appearance */

const THEME_KEY = "catussh.site.theme";

function applyTheme(dark) {
  document.documentElement.classList.toggle("dark", dark);
  try {
    localStorage.setItem(THEME_KEY, dark ? "dark" : "light");
  } catch {
    // Private mode; the toggle still works for this visit.
  }
}

function initialTheme() {
  // ?theme=dark wins, so a link or a screenshot can force either appearance.
  const forced = new URLSearchParams(location.search).get("theme");
  if (forced === "dark") return true;
  if (forced === "light") return false;

  try {
    const saved = localStorage.getItem(THEME_KEY);
    if (saved) return saved === "dark";
  } catch {
    // Fall through to the system preference.
  }
  return window.matchMedia("(prefers-color-scheme: dark)").matches;
}

applyTheme(initialTheme());

document.addEventListener("click", (event) => {
  if (!event.target.closest("[data-theme-toggle]")) return;
  applyTheme(!document.documentElement.classList.contains("dark"));
});

/* ------------------------------------------------------------ hero shot */

/**
 * Renders three of the app's monitor cards into the hero frame.
 *
 * A frame this size cannot hold all seven legibly — shrinking them is how a
 * product shot ends up unreadable. So it shows the two that carry the most
 * meaning (Load, Processor) and the one that visibly moves (Network), using the
 * app's own card functions at a size a person can read.
 */
function renderHero(payload) {
  const mount = document.getElementById("hero-dashboard");
  if (!mount) return;

  // The cards come from monitor.js. If that file failed to load, say so plainly
  // instead of throwing an opaque ReferenceError into the console.
  const cardsAvailable =
    typeof systemCard === "function" &&
    typeof processorCard === "function" &&
    typeof networkCard === "function" &&
    typeof monitorFacts === "function";

  if (!cardsAvailable || !payload?.status) {
    mount.innerHTML = `<div class="rx-card rx-empty-state">
      <span class="rx-section-title">Monitor preview</span>
      <span class="rx-help-text">The reading could not be loaded.</span>
    </div>`;
    return;
  }

  const { status } = payload;
  mount.innerHTML = `
    <div class="rx-stack-3" style="margin-bottom:var(--space-4)">
      <div class="rx-row-2">
        <span class="rx-page-title" style="font-size:20px">${esc(status.system.hostname || "web-01")}</span>
        <span class="rx-tag rx-tag--accent">live</span>
      </div>
      ${monitorFacts(status)}
    </div>
    <div class="rx-stack-4">
      ${systemCard(status)}
      ${processorCard(status, state, 1)}
      ${networkCard(status, 2)}
    </div>`;
}

async function loadHero() {
  let payload = null;
  try {
    const response = await fetch("monitor-fixture.json", { cache: "no-store" });
    if (response.ok) payload = await response.json();
  } catch {
    payload = null;
  }
  renderHero(payload);
  return payload;
}

loadHero();
