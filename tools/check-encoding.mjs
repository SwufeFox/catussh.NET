// Reports U+FFFD (replacement characters) in the design sources. A file that
// picks these up has been re-encoded wrongly somewhere; the shell cannot tell a
// literal ellipsis from mojibake, so this checks it explicitly.
import { readFileSync, readdirSync, statSync } from "node:fs";
import { join, relative } from "node:path";

const root = process.argv[2] ?? "src";
const files = [];
const walk = (dir) => {
  for (const entry of readdirSync(dir)) {
    const path = join(dir, entry);
    if (statSync(path).isDirectory()) {
      if (entry === "node_modules" || entry === "bin" || entry === "obj") continue;
      walk(path);
    } else if (/\.(js|mjs|css|html|cs|json|md)$/.test(entry)) {
      files.push(path);
    }
  }
};
walk(root);

let bad = 0;
for (const file of files) {
  const text = readFileSync(file, "utf8");
  const count = [...text].filter((c) => c === "\uFFFD").length;
  if (count > 0) {
    bad++;
    console.log(`${relative(process.cwd(), file)}: ${count} replacement character(s)`);
  }
}
console.log(bad === 0 ? `clean (${files.length} files)` : `${bad} file(s) need re-encoding`);
process.exit(bad === 0 ? 0 : 1);
