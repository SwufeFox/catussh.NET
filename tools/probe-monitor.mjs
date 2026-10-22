// Probe: does the monitor page's data path actually deliver a reading?
//
//   node tools/probe-monitor.mjs            # static server on :5199
//
// Runs the page's own code in Node (no browser): loads monitor.js, replays the
// same fetch the page does, and prints what the cards would contain. Checks the
// two halves separately — the HTTP payload and the render — so a failure says
// which one broke.
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";
import vm from "node:vm";

const here = dirname(fileURLToPath(import.meta.url));
const root = join(here, "..");
const base = process.argv[2] ?? "http://127.0.0.1:5199";

const source = readFileSync(join(root, "src/Catussh.Design/monitor.js"), "utf8");

// The page's globals needed by monitor.js; render() lives in app.js, so stub it.
const sandbox = {
  console,
  fetch: (url) => fetch(new URL(url, base)),
  render: () => {},
  setPage: () => {},
  toolbar: () => "",
  // monitor.js reads the bridge port from location.search in a browser.
  location: { search: "", hash: "#monitor" },
  URLSearchParams,
  state: { monitorCores: false, monitorSwap: false },
};
vm.createContext(sandbox);
// Top-level `const` does not become a property of the context object, so ask the
// script to hand its bindings over explicitly.
vm.runInContext(
  source +
    "\n;globalThis.__probe = { RXFormat, monitorPage, loadMonitorSnapshot };",
  sandbox,
);
Object.assign(sandbox, sandbox.__probe);

const payload = await sandbox.loadMonitorSnapshot();
if (!payload) {
  console.error("FAIL loadMonitorSnapshot returned null");
  process.exit(1);
}

const status = payload.status;
const cards = [
  ["Load", sandbox.RXFormat.oneDecimal(status.system.load1)],
  ["Processor", sandbox.RXFormat.oneDecimal(status.processor.summary.sumUsed)],
  ["Receive", sandbox.RXFormat.rateString(payload.totals.receivePerSecond)],
  ["Memory", sandbox.RXFormat.bytesString(status.memory.memTotal * 1024)],
  ["Uptime", sandbox.RXFormat.duration(status.system.uptimeSec)],
];

console.log("payload");
for (const [label, value] of cards) console.log(`  ${label.padEnd(10)} ${value}`);
console.log(`  interfaces ${status.network.elements.length}, disks ${status.fileSystem.elements.length}, gpus ${status.graphics?.units.length ?? 0}, history ${payload.history.receive.length}`);

// Render the page with the payload and confirm the cards are in the markup.
sandbox.state.monitor = payload;
const html = sandbox.monitorPage(sandbox.state);
for (const needle of ["Disks", "Network", "Graphics", "Sparkline" /* not in html */].slice(0, 3)) {
  if (!html.includes(needle)) {
    console.error(`FAIL rendered page is missing "${needle}"`);
    process.exit(1);
  }
}
if (!html.includes("polyline")) {
  console.error("FAIL no sparkline polyline in the rendered page");
  process.exit(1);
}
console.log("render\n  cards present, sparkline drawn");
console.log("\nmonitor data path ok");
