/*
 * gen-monitor-fixture.mjs — writes src/Catussh.Design/monitor-fixture.json.
 *
 * The Monitor page can be opened without the .NET host (node tools/serve.mjs),
 * and then it needs a reading to draw. Rather than hand-writing a second copy of
 * the demo data, this runs the SAME fixture through the SAME parser: it shells
 * out to the .NET project, which prints the host payload for the page.
 */
import { execFileSync } from "node:child_process";
import { writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const here = dirname(fileURLToPath(import.meta.url));
const root = join(here, "..");
// The `dotnet` on PATH here resolves to a runtime-only x86 install that reports
// "No .NET SDKs were found"; the real SDK lives in Program Files.
const dotnet = process.env.DOTNET ?? "C:\\Program Files\\dotnet\\dotnet.exe";

const json = execFileSync(
  dotnet,
  [
    "run",
    "--project",
    join(root, "src/Catussh.App/Catussh.App.csproj"),
    "--no-build",
    "--",
    "--dump-snapshot",
  ],
  { encoding: "utf8" },
);

const parsed = JSON.parse(json.slice(json.indexOf("{")));
writeFileSync(join(root, "src/Catussh.Design/monitor-fixture.json"), JSON.stringify(parsed, null, 2) + "\n");
console.log(`wrote monitor-fixture.json (${parsed.status.network.elements.length} interfaces)`);
