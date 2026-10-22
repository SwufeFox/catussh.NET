// ui-probe.mjs — drives the real page in a real browser and reports what rendered.
//
//   node tools/ui-probe.mjs <apiPort> [page]
//
// Serves the design files through tools/serve.mjs's static handler equivalent
// (file:// is enough — the page only needs the api port in the query string),
// clicks through a flow, and prints the resulting text so behaviour is verified
// instead of eyeballed.
import { execFileSync } from "node:child_process";
import { mkdtempSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";

const apiPort = process.argv[2];
const page = process.argv[3] ?? "home";
if (!apiPort) {
  console.error("usage: node tools/ui-probe.mjs <apiPort> [page]");
  process.exit(2);
}

const chrome = "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe";
const root = fileURLToPath(new URL("../src/Catussh.Design/", import.meta.url)).replaceAll("\\", "/");

// A harness that frames the page, waits for its fetches, then reports the DOM as
// text. --dump-dom cannot see timer-driven updates, so this polls the iframe.
const harness = `<!DOCTYPE html><meta charset="utf-8">
<iframe id="f" src="file:///${root}index.html?api=${apiPort}#${page}"
        style="width:1400px;height:900px;border:0"></iframe>
<pre id="out">pending</pre>
<script>
const f = document.getElementById('f');
const report = {};
let ticks = 0;

f.addEventListener('load', () => {
  const timer = setInterval(() => {
    ticks++;
    const d = f.contentDocument;
    if (!d) return;
    const app = d.getElementById('app');
    const text = (sel) => (app.querySelector(sel) || {}).textContent || '';

    report.title = text('.rx-page-title');
    report.sidebarCounts = [...app.querySelectorAll('.rx-sidebar__count')].map(n => n.textContent);
    report.cards = [...app.querySelectorAll('.rx-server-card__name')].map(n => n.textContent);
    report.emptyState = text('.rx-empty-state');
    report.monitorPhase = d.querySelector('.rx-monitor')
      ? 'dashboard'
      : (app.querySelector('.rx-monitor-placeholder') ? 'placeholder' : 'none');
    report.monitorTags = [...app.querySelectorAll('.rx-tag')].map(n => n.textContent.trim());
    report.toast = (d.querySelector('.rx-toast') || {}).textContent || '';

    // Give the page up to ~4s to settle, then write the answer out.
    if (ticks >= 12) {
      clearInterval(timer);
      document.getElementById('out').textContent = JSON.stringify(report, null, 2);
    }
  }, 350);
});
</script>`;

const dir = mkdtempSync(join(tmpdir(), "catussh-ui-"));
const file = join(dir, "probe.html");
writeFileSync(file, harness, "utf8");
const profile = mkdtempSync(join(tmpdir(), "catussh-chrome-"));

const dom = execFileSync(
  chrome,
  [
    "--headless=new",
    "--disable-gpu",
    "--no-first-run",
    "--allow-file-access-from-files",
    `--user-data-dir=${profile}`,
    "--virtual-time-budget=12000",
    "--dump-dom",
    `file:///${file.replaceAll("\\", "/")}`,
  ],
  { encoding: "utf8", stdio: ["ignore", "pipe", "ignore"] },
);

const match = dom.match(/<pre id="out">([\s\S]*?)<\/pre>/);
if (!match) {
  console.error("probe produced no output (the page may have thrown)");
  process.exit(1);
}
console.log(
  match[1]
    .replaceAll("&quot;", '"')
    .replaceAll("&amp;", "&")
    .replaceAll("&lt;", "<")
    .replaceAll("&gt;", ">"),
);
