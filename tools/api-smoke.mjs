// api-smoke.mjs — exercises the host's loopback API the way the UI does.
//
//   node tools/api-smoke.mjs <port>
//
// Add a server, list it, add an identity, start a monitor session, read its phase,
// then remove the server. A failure names the route that broke, so a UI bug and a
// host bug stay distinguishable.
const port = Number(process.argv[2]);
if (!port) {
  console.error("usage: node tools/api-smoke.mjs <port>");
  process.exit(2);
}

const base = `http://127.0.0.1:${port}`;
let failures = 0;

const check = (ok, message) => {
  console.log(`  ${ok ? "ok  " : "FAIL"} ${message}`);
  if (!ok) failures++;
};

async function call(path, options = {}) {
  const response = await fetch(base + path, {
    headers: { "content-type": "application/json" },
    ...options,
  });
  const text = await response.text();
  let body = null;
  try {
    body = text ? JSON.parse(text) : null;
  } catch {
    body = text;
  }
  return { status: response.status, body };
}

console.log("health");
const health = await call("/health");
check(health.status === 200 && health.body?.ok === true, `GET /health -> ${health.status}`);

console.log("machines");
const created = await call("/machines", {
  method: "POST",
  body: JSON.stringify({ name: "smoke-01", address: "127.0.0.1", port: "22", group: "Smoke", comment: "test" }),
});
check(created.status === 201, `POST /machines -> ${created.status}`);
const machineId = created.body?.id;

const listed = await call("/machines");
const groups = listed.body?.machines ?? [];
const found = groups.flatMap((g) => g.items).find((m) => m.id === machineId);
check(Boolean(found), "the new server appears in the list");
check(found?.group === "Smoke", `it is grouped as ${found?.group}`);

console.log("quick connect");
const quick = await call("/machines", { method: "POST", body: JSON.stringify({ quickConnect: "root@example.com:2222" }) });
check(quick.status === 201, `user@host:port -> ${quick.status}`);
check(quick.body?.address === "example.com", `address parsed as ${quick.body?.address}`);
check(quick.body?.port === 2222, `port parsed as ${quick.body?.port}`);

const badQuick = await call("/machines", { method: "POST", body: JSON.stringify({ quickConnect: "not a host" }) });
check(badQuick.status === 400, `garbage quick connect rejected -> ${badQuick.status}`);

console.log("identities");
const identity = await call("/identities", {
  method: "POST",
  body: JSON.stringify({ username: "smoke", password: "hunter2", automatic: true }),
});
check(identity.status === 201, `POST /identities -> ${identity.status}`);

const identities = await call("/identities");
const saved = (identities.body ?? []).find((i) => i.id === identity.body?.id);
check(Boolean(saved), "the identity is stored");
check(saved?.hasSecret === true, "it reports a stored secret");

console.log("monitor");
// 127.0.0.1:22 is almost certainly refused here; the point is that the session
// reports a phase and a reason instead of hanging.
const started = await call(`/monitor/${machineId}`, { method: "POST" });
check(started.status === 200, `POST /monitor/{id} -> ${started.status}`);

let phase = null;
let last = null;
for (let attempt = 0; attempt < 30; attempt++) {
  await new Promise((r) => setTimeout(r, 500));
  const poll = await call(`/monitor/${machineId}`);
  last = poll.body;
  phase = poll.body?.phase;
  if (phase && phase !== "connecting") break;
}
check(phase !== null, `the session settles on a phase: ${phase}`);
check(
  phase === "failed" || phase === "lost" || phase === "connected",
  `the phase is a real outcome (${phase})`,
);
if (phase === "failed") {
  check(typeof last?.error === "string" && last.error.length > 0, `it explains why: ${last?.error}`);
}

console.log("cleanup");
const removed = await call(`/machines/${machineId}`, { method: "DELETE" });
check(removed.status === 200, `DELETE /machines/{id} -> ${removed.status}`);
const removedIdentity = await call(`/identities/${identity.body?.id}`, { method: "DELETE" });
check(removedIdentity.status === 200, `DELETE /identities/{id} -> ${removedIdentity.status}`);

// The quick-connect server from the earlier step.
if (quick.body?.id) await call(`/machines/${quick.body.id}`, { method: "DELETE" });

console.log(failures === 0 ? "\napi smoke passed" : `\n${failures} api check(s) failed`);
process.exit(failures === 0 ? 0 : 1);
