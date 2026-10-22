/*
 * design.spec.mjs — guards the design-system port against quiet regressions.
 *
 *   node tests/Catussh.Design.Tests/design.spec.mjs
 *
 * The check that matters most: every palette colour in Tokens.swift must appear
 * verbatim in tokens.css. The design system's whole value is that these are the
 * Swift numbers, so a hand-picked replacement shade is a bug, not a tweak.
 */

import { readFileSync, existsSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const here = dirname(fileURLToPath(import.meta.url));
const root = join(here, "..", "..");
// The port owns this repository; the Swift original is not in the tree. _src/ is a
// local read-only checkout kept for comparison, and the token parity check is
// skipped (loudly) when it is absent.
const swiftTokens = join(root, "_src/Foundation/CatusshDesign/Sources/CatusshDesign/Tokens.swift");
const cssTokens = join(root, "src/Catussh.Design/tokens.css");

let failures = 0;
const check = (ok, message) => {
  if (ok) {
    console.log(`  ok   ${message}`);
  } else {
    failures++;
    console.error(`  FAIL ${message}`);
  }
};

/* ---------------------------------------------------------------- tokens */

console.log("tokens");
const css = readFileSync(cssTokens, "utf8");

if (!existsSync(swiftTokens)) {
  console.log("  skip  Swift reference not cloned (_src missing)");
} else {
  const swift = readFileSync(swiftTokens, "utf8");
  const literals = [...swift.matchAll(/Color\(rxLight: 0x([0-9A-Fa-f_]+), dark: 0x([0-9A-Fa-f_]+)\)/g)];
  check(literals.length > 0, `found ${literals.length} colour literals in Tokens.swift`);

  // The Swift literal is 0xRRGGBBAA; the CSS token is rgba(r, g, b, a).
  // Compare RGB exactly and alpha numerically (the CSS may write 0.2 for 0x33).
  const toChannels = (hex) => {
    const clean = hex.replaceAll("_", "").padEnd(8, "F");
    return [0, 2, 4, 6].map((i) => parseInt(clean.slice(i, i + 2), 16));
  };

  const cssRgba = [...css.matchAll(/rgba\((\d+),\s*(\d+),\s*(\d+),\s*([\d.]+)\)/g)].map((m) => [
    Number(m[1]), Number(m[2]), Number(m[3]), Number(m[4]),
  ]);

  const missing = [];
  for (const [, light, dark] of literals) {
    for (const hex of [light, dark]) {
      const [r, g, b, a] = toChannels(hex);
      const alpha = Math.round((a / 255) * 1000) / 1000;
      const found = cssRgba.some(
        ([cr, cg, cb, ca]) => cr === r && cg === g && cb === b && Math.abs(ca - alpha) <= 0.01,
      );
      if (!found) missing.push(`${hex} (rgb ${r},${g},${b} a=${alpha})`);
    }
  }
  check(
    missing.length === 0,
    missing.length === 0
      ? "every Swift colour literal has a tokens.css counterpart"
      : `missing colours: ${[...new Set(missing)].join(", ")}`,
  );
}

for (const [name, value] of [
  ["--space-4", "16px"],
  ["--control-height", "28px"],
  ["--small-control-height", "22px"],
  ["--radius-card", "18px"],
  ["--sidebar-width", "216px"],
]) {
  check(new RegExp(`${name}:\\s*${value}`).test(css), `${name} is ${value}`);
}

/* ------------------------------------------------------------ table fit */

/*
 * The table has this shape (ServersView.swift): five fixed columns plus a
 * flexible name column. At the default 1440px window everything must fit,
 * otherwise the name collapses to 0px and the row loses its server name.
 * Widths live in components.css, so read them from there, not from a copy.
 */
console.log("table fit");

// Column widths live in components.css; the token checks above read tokens.css.
const components = readFileSync(join(root, "src/Catussh.Design/components.css"), "utf8");

// The checked-out file uses CRLF; accept either line ending.
const widthOf = (suffix) => {
  const match = components.match(new RegExp(`\\.rx-table__cell--${suffix}\\s*\\{\\s*width:\\s*(\\d+)px`));
  return match ? Number(match[1]) : NaN;
};

const fixed = ["address", "identity", "cpu", "last-used", "actions"].map(widthOf);
check(fixed.every(Number.isFinite), `fixed column widths read: ${fixed.join(", ")}px`);

const nameFloorMatch = components.match(/\.rx-table__cell--name\s*\{[^}]*?min-width:\s*(\d+)px/);
const nameFloor = Number(nameFloorMatch?.[1]);
check(Number.isFinite(nameFloor) && nameFloor > 0, `name column has a ${nameFloor}px floor`);

// sidebar 216 + page padding 48*2 + card padding 16*2 + row padding 12*2
const chrome = 216 + 96 + 32 + 24;
const needed = fixed.reduce((sum, n) => sum + n, 0) + chrome + nameFloor;
check(needed <= 1440, `columns need ${needed}px at minimum, which fits the 1440px window`);

// And the collapse thresholds must be the ones ServerColumns.swift derives.
const thresholds = [...components.matchAll(/@container \(max-width: (\d+)px\)/g)].map((m) => Number(m[1]));
check(
  [584, 694, 814].every((t) => thresholds.includes(t)),
  `column-collapse thresholds match ServersView.swift (${thresholds.join(", ")})`,
);

console.log(failures === 0 ? "\nall design checks passed" : `\n${failures} design check(s) failed`);
process.exit(failures === 0 ? 0 : 1);
