/** Result returned by runtime decoders at external-data boundaries. */
export type DecodeResult<T> =
  | { readonly ok: true; readonly value: T }
  | { readonly ok: false }

/** Creates a successful decoder result. */
export function decoded<T>(value: T): DecodeResult<T> {
  return { ok: true, value }
}

/** Shared failed decoder result. */
export const invalid: DecodeResult<never> = { ok: false }

/** Narrows an unknown JSON value to an object with string keys. */
export function isJsonObject(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value)
}

/**
 * Reads JSON without claiming a domain type. Missing and malformed files
 * return `undefined`; adapters must decode the resulting `unknown` value.
 */
export async function readJsonFile(path: string): Promise<unknown | undefined> {
  const file = Bun.file(path)
  if (!(await file.exists())) {
    return undefined
  }
  try {
    return JSON.parse(await file.text())
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
