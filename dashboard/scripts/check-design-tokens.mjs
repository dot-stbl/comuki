#!/usr/bin/env node
// Design-system drift guard (Phase 3 DoD #4).
//
// Compares the load-bearing `--st-*` status-token vocabulary between the
// design system (source of truth) and the dashboard. Catches STRUCTURAL
// drift: a status token added/removed in one file but not the other.
//
// Scope limitation (deliberate): a full value-level, theme-aware diff is
// non-trivial because the two files use different theme-block structures
// (tokens.css: `:root` dark + `[data-theme="light"]`; index.css: `:root`
// light + `.dark` dark) and tokens.css carries some overridden duplicates.
// The shared concrete status hexes currently match (audited 2026-07-06);
// this script guards the token SET so a new/removed status can't silently
// diverge. A value-level checker can layer on later.
import { readFileSync } from "node:fs";

const designPath = "../.agents/docs/design-system/styles/tokens.css";
const dashboardPath = "src/index.css";

const design = readFileSync(designPath, "utf8");
const dashboard = readFileSync(dashboardPath, "utf8");

// `--st-…` (double hyphen) distinguishes real status tokens from the
// Tailwind `--color-st-…` mapping (single hyphen) in index.css.
const statusNames = (text) =>
  [...new Set([...text.matchAll(/--st-[a-z-]+/g)].map((match) => match[0]))].sort();

const designNames = statusNames(design);
const dashboardNames = statusNames(dashboard);

const designSet = new Set(designNames);
const dashboardSet = new Set(dashboardNames);

const onlyInDesign = designNames.filter((name) => !dashboardSet.has(name));
const onlyInDashboard = dashboardNames.filter((name) => !designSet.has(name));

if (onlyInDesign.length > 0 || onlyInDashboard.length > 0) {
  console.error("✗ Design-system status-token drift detected:\n");
  if (onlyInDesign.length > 0) {
    console.error("  only in design system (missing from dashboard):");
    for (const name of onlyInDesign) {
      console.error(`    ${name}`);
    }
  }
  if (onlyInDashboard.length > 0) {
    console.error("  only in dashboard (missing from design system):");
    for (const name of onlyInDashboard) {
      console.error(`    ${name}`);
    }
  }
  console.error("\nSync the --st-* tokens between the two files.");
  process.exit(1);
}

console.log(
  `✓ ${designNames.length} --st-* status tokens match between design system and dashboard`
);
