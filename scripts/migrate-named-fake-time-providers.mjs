#!/usr/bin/env bun
/* migrate-named-fake-time-providers.mjs — мигрирует локальные named
 * копии FakeTimeProvider (MergeBatchFakeTimeProvider, MergeQueueFakeTimeProvider,
 * VerificationFakeTimeProvider, MutableFakeTimeProvider) в пакетный
 * Microsoft.Extensions.Time.Testing.FakeTimeProvider.
 *
 * Запуск: bun scripts/migrate-named-fake-time-providers.mjs <path>...
 */

import { readFileSync, writeFileSync, readdirSync, statSync } from 'node:fs';
import { relative } from 'node:path';

const NAMES = [
  'MergeBatchFakeTimeProvider',
  'MergeQueueFakeTimeProvider',
  'VerificationFakeTimeProvider',
  'MutableFakeTimeProvider',
];

let totalFiles = 0;
let totalRenames = 0;

function walk(dir) {
  for (const entry of readdirSync(dir)) {
    if (entry === 'bin' || entry === 'obj' || entry === 'node_modules' || entry === '.git') continue;
    const full = `${dir}/${entry}`;
    const s = statSync(full);
    if (s.isDirectory()) walk(full);
    else if (entry.endsWith('.cs')) processFile(full);
  }
}

function processFile(path) {
  const text = readFileSync(path, 'utf8');
  const usesAny = NAMES.some((n) => text.includes(n));
  if (!usesAny) return;
  let modified = false;

  let updated = text;
  for (const name of NAMES) {
    const re = new RegExp(`\\bnew\\s+${name}\\b`, 'g');
    const m = updated.match(re);
    if (!m) continue;
    updated = updated.replace(re, 'new FakeTimeProvider');
    totalRenames += m.length;
    modified = true;
  }

  if (!updated.includes('using Microsoft.Extensions.Time.Testing;')) {
    const usings = [...updated.matchAll(/^using [^\n]+;\n/gm)];
    if (usings.length > 0) {
      const last = usings[usings.length - 1];
      const idx = last.index + last[0].length;
      updated = updated.slice(0, idx) + 'using Microsoft.Extensions.Time.Testing;\n' + updated.slice(idx);
      modified = true;
    } else {
      updated = 'using Microsoft.Extensions.Time.Testing;\n' + updated;
      modified = true;
    }
  }

  if (modified) {
    writeFileSync(path, updated, 'utf8');
    totalFiles += 1;
    totalRenames += 0;
    process.stdout.write(`  ${relative(process.cwd(), path)}\n`);
  }
}

const roots = process.argv.slice(2);
for (const root of roots) {
  const s = statSync(root);
  if (s.isDirectory()) walk(root);
}

process.stdout.write(`\nTOTAL: ${totalFiles} files touched\n`);
