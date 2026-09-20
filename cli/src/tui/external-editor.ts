/**
 * $EDITOR seam for the OpenTUI composer (issue #75): write the draft
 * to a temp file, hand it to `$VISUAL`/`$EDITOR`, adopt the returned
 * text. Spawned ONLY on the production path — tests inject a fake
 * editor function through `TuiHostOptions.externalEditor` and never
 * touch this module's process spawning.
 */

import { spawn } from "node:child_process"
import { readFileSync, rmSync, writeFileSync } from "node:fs"
import { join } from "node:path"
import { tmpdir } from "node:os"

let tempCounter = 0

/**
 * Resolve the editor command: `$VISUAL` wins over `$EDITOR` (the
 * POSIX convention); an explicit empty string counts as unset.
 */
export function resolveEditorCommand(): string | null {
  const visual = process.env.VISUAL
  if (visual !== undefined && visual.trim().length > 0) {
    return visual
  }
  const editor = process.env.EDITOR
  if (editor !== undefined && editor.trim().length > 0) {
    return editor
  }
  return null
}

/**
 * Edit `draft` in the external editor and return the adopted text.
 * Throws when no editor is configured or the editor exits non-zero —
 * the host's `suspendForEdit` seam turns the throw into an error
 * result and the draft stays untouched.
 */
export async function spawnExternalEditor(draft: string): Promise<string> {
  const editor = resolveEditorCommand()
  if (editor === null) {
    throw new Error("no external editor: set $VISUAL or $EDITOR")
  }
  tempCounter += 1
  const filePath = join(
    tmpdir(),
    `comuki-draft-${process.pid}-${tempCounter}.md`
  )
  writeFileSync(filePath, draft, "utf8")
  try {
    const exitCode = await new Promise<number>((resolve, reject) => {
      const child = spawn(editor, [filePath], {
        stdio: "inherit",
      })
      child.on("error", reject)
      child.on("exit", (code) => {
        resolve(code ?? 0)
      })
    })
    if (exitCode !== 0) {
      throw new Error(`external editor exited with code ${exitCode}`)
    }
    return readFileSync(filePath, "utf8").replace(/\n$/, "")
  } finally {
    rmSync(filePath, { force: true })
  }
}
