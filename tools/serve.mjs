// Minimal static server for the design preview: `node tools/serve.mjs [port]`.
// Serves src/Catussh.Design so the page can be opened without file:// quirks.
import { createServer } from "node:http";
import { readFile } from "node:fs/promises";
import { extname, join, normalize } from "node:path";
import { fileURLToPath } from "node:url";

const root = fileURLToPath(new URL("../src/Catussh.Design/", import.meta.url));
const port = Number(process.argv[2] ?? 5199);

const types = {
  ".html": "text/html; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".svg": "image/svg+xml",
  ".woff2": "font/woff2",
};

createServer(async (req, res) => {
  const url = new URL(req.url, "http://localhost");
  const rel = normalize(decodeURIComponent(url.pathname === "/" ? "/index.html" : url.pathname));
  if (rel.includes("..")) {
    res.writeHead(403).end();
    return;
  }
  try {
    const body = await readFile(join(root, rel));
    res.writeHead(200, { "content-type": types[extname(rel)] ?? "application/octet-stream" });
    res.end(body);
  } catch {
    res.writeHead(404).end("not found");
  }
}).listen(port, "127.0.0.1", () => {
  console.log(`catussh design preview → http://127.0.0.1:${port}`);
});
