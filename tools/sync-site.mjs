/*
 * sync-site.mjs — copies the app's design system into site/ before a deploy.
 *
 *   node tools/sync-site.mjs
 *
 * The product page must look like the app, and the only way to guarantee that is
 * to use the same files. Anything the page needs from src/Catussh.Design is
 * copied here; site.css and site.js are the page's own and are never touched.
 */
import { copyFileSync, existsSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { join } from "node:path";

const root = fileURLToPath(new URL("..", import.meta.url));
const from = join(root, "src/Catussh.Design");
const to = join(root, "site");

/** [source, destination] pairs. */
const files = [
  ["tokens.css", "tokens.css"],
  ["components.css", "components.css"],
  ["monitor.css", "monitor.css"],
  // The card functions the hero renders with.
  ["monitor.js", "monitor.js"],
  // The reading those cards display, produced by the real parsers.
  ["monitor-fixture.json", "monitor-fixture.json"],
];

let missing = 0;
for (const [source, destination] of files) {
  const src = join(from, source);
  if (!existsSync(src)) {
    console.error(`missing: ${src}`);
    missing++;
    continue;
  }
  copyFileSync(src, join(to, destination));
  console.log(`copied ${source}`);
}

if (missing > 0) {
  console.error(`\n${missing} file(s) missing — run from the repository root and make sure src/Catussh.Design exists.`);
  process.exit(1);
}

console.log("\nsite/ is in sync with the app's design system");
