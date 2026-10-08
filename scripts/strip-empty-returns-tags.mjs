#!/usr/bin/env bun
/* strip-empty-returns-tags.mjs — удаляет ВСЕ пустые <returns></returns> теги.
 * <returns> не относится к параметрам сигнатуры — безопасно удалять.
 * Запуск: bun scripts/strip-empty-returns-tags.mjs <path>...
 */

import { readFileSync, writeFileSync, readdirSync, statSync } from 'node:fs';
import { relative } from 'node:path';

const EMPTY_RETURNS_LINE = /^[ \t]*\/\/\/\s*<returns><\/returns>[ \t]*$/;
const EMPTY_RETURNS_BODY = /<returns><\/returns>/g;

let totalFiles = 0;
let totalRemoved = 0;

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
  const original = readFileSync(path, 'utf8');
  const updated = original.replace(EMPTY_RETURNS_BODY, () => { totalRemoved++; return ''; });
  if (updated !== original) {
    writeFileSync(path, updated, 'utf8');
    totalFiles += 1;
    process.stdout.write(`  ${relative(process.cwd(), path)}: -1 empty returns\n`);
  }
}

const roots = process.argv.slice(2);
for (const root of roots) {
  const s = statSync(root);
  if (s.isDirectory()) walk(root);
}

process.stdout.write(`\nTOTAL: ${totalRemoved} empty <returns> tags removed; ${totalFiles} files touched\n`);
