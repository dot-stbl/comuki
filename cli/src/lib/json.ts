/**
 * Tiny shared JSON-file helpers used by both `config.json` and
 * `sessions.json`: missing/malformed files read as `undefined`, writes
 * create the parent directory with owner-only permissions.
 */

export async function readJsonFile<T>(path: string): Promise<T | undefined> {
  const file = Bun.file(path)
  if (!(await file.exists())) {
    return undefined
  }
  try {
    return JSON.parse(await file.text()) as T
  } catch {
    return undefined
  }
}

export async function writeJsonFile(
  path: string,
  contents: unknown
): Promise<void> {
  await Bun.write(path, JSON.stringify(contents, null, 2) + "\n", {
    createPath: true,
    mode: 0o600,
  })
}
