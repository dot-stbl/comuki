#!/usr/bin/env bun
/* strip-empty-param-tags-only-all-empty-docs.mjs — удаляет самозакрытые
 * пустые <param name="X"></param> теги, НО ТОЛЬКО в документационных
 * блоках, где ВСЕ <param> теги пустые (не остаётся partial-docs).
 *
 * Запуск: bun scripts/strip-empty-param-tags-only-all-empty-docs.mjs <path>...
 */

import { readFileSync, writeFileSync, readdirSync, statSync } from 'node:fs';
import { relative } from 'node:path';

const EMPTY_PARAM_LINE = /^[ \t]*\/\/\/\s*<param name="[^"]+"><\/param>[ \t]*$/;
const HAS_CONTENT_PARAM = /<param name="[^"]+"[^>]*>[\s\S]+?<\/param>/;

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
  const lines = original.split('\n');
  let removed = 0;
  let i = 0;

  while (i < lines.length) {
    // Find `<summary>` start of a doc block.
    const summaryLine = lines.findIndex((l, idx) => idx >= i && l.trim().startsWith('/// <summary>'));
    if (summaryLine < 0) break;

    let j = summaryLine;
    while (j < lines.length && lines[j].trim().startsWith('///')) j += 1;

    // Block ends at j. Block must NOT contain `<inheritdoc />` (would inherit).
    const blockText = lines.slice(summaryLine, j).join('\n');

    // Skip the block if it has any `<inheritdoc />` (don't know what was inherited).
    if (blockText.includes('<inheritdoc />')) {
      i = j;
      continue;
    }

    if (HAS_CONTENT_PARAM.test(blockText)) {
      // Block has content tags — skip to avoid creating partial docs.
      i = j;
      continue;
    }

    // Block has only `<param>` empty tags (or none) — remove them line-by-line.
    for (let k = summaryLine; k < j; k++) {
      const line = lines[k];
      if (EMPTY_PARAM_LINE.test(line)) {
        lines.splice(k, 1);
        j -= 1;
        k -= 1;
        removed += 1;
      }
    }

    i = j; // skip past the block (whether stripped or not)
  }

  const updated = lines.join('\n');
  if (updated !== original) {
    writeFileSync(path, updated, 'utf8');
    totalFiles += 1;
    totalRemoved += removed;
    process.stdout.write(`  ${relative(process.cwd(), path)}: -${removed} empty tags\n`);
  }
}

const roots = process.argv.slice(2);
for (const root of roots) {
  const s = statSync(root);
  if (s.isDirectory()) walk(root);
}

process.stdout.write(`\nTOTAL: ${totalRemoved} empty <param> tags removed (only in all-empty docs); ${totalFiles} files touched\n`);
