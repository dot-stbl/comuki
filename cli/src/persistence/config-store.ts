import { randomUUID } from "node:crypto"
import { chmod, mkdir, readFile, rename, writeFile } from "node:fs/promises"
import { homedir } from "node:os"
import { dirname, join } from "node:path"
import {
  decoded,
  invalid,
  isJsonObject,
  type DecodeResult,
} from "../lib/json"

export interface ConfigFileContents {
  readonly [key: string]: unknown
  readonly url?: string
  readonly apiKey?: string
  readonly tenant?: string
  readonly cookie?: string
  readonly defaultProject?: string
  readonly theme?: string
  readonly bell?: boolean
  readonly preferredProfile?: string
  readonly contextWindow?: number
}

export type ConfigMutator = (
  current: ConfigFileContents
) => ConfigFileContents | Promise<ConfigFileContents>

/** Persistence seam for CLI configuration. */
export interface ConfigStore {
  read(): Promise<ConfigFileContents>
  update(mutator: ConfigMutator): Promise<ConfigFileContents>
}

const CONFIG_STRING_FIELDS = [
  "url",
  "apiKey",
  "tenant",
  "cookie",
  "defaultProject",
  "theme",
  "preferredProfile",
] as const

/**
 * Decodes config JSON while retaining extension fields owned by newer CLI
 * versions. Known fields with invalid types are ignored rather than leaked.
 */
export function decodeConfigFile(value: unknown): DecodeResult<ConfigFileContents> {
  if (!isJsonObject(value)) {
    return invalid
  }

  const contents: Record<string, unknown> = { ...value }
  for (const field of CONFIG_STRING_FIELDS) {
    if (field in contents && typeof contents[field] !== "string") {
      delete contents[field]
    }
  }
  if ("bell" in contents && typeof contents.bell !== "boolean") {
    delete contents.bell
  }
  if (
    "contextWindow" in contents &&
    (typeof contents.contextWindow !== "number" ||
      !Number.isFinite(contents.contextWindow))
  ) {
    delete contents.contextWindow
  }

  return decoded(contents)
}

export function configDir(
  xdgConfigHome: string | undefined = process.env.XDG_CONFIG_HOME
): string {
  const base = xdgConfigHome?.trim() || join(homedir(), ".config")
  return join(base, "comuki")
}

export function configFilePath(
  xdgConfigHome: string | undefined = process.env.XDG_CONFIG_HOME
): string {
  return join(configDir(xdgConfigHome), "config.json")
}

/** JSON adapter for the ConfigStore seam. */
export class JsonConfigStore implements ConfigStore {
  private pending: Promise<void> = Promise.resolve()

  constructor(private readonly path: string = configFilePath()) {}

  async read(): Promise<ConfigFileContents> {
    try {
      const parsed: unknown = JSON.parse(await readFile(this.path, "utf8"))
      const result = decodeConfigFile(parsed)
      return result.ok ? result.value : {}
    } catch {
      return {}
    }
  }

  update(mutator: ConfigMutator): Promise<ConfigFileContents> {
    const operation = this.pending.then(async () => {
      const updated = await mutator(await this.read())
      await writeConfigAtomically(this.path, updated)
      return updated
    })
    this.pending = operation.then(
      () => undefined,
      () => undefined
    )
    return operation
  }
}

export const configStore: ConfigStore = new JsonConfigStore()

async function writeConfigAtomically(
  path: string,
  contents: ConfigFileContents
): Promise<void> {
  const directory = dirname(path)
  const temporaryPath = `${path}.${process.pid}.${randomUUID()}.tmp`
  await mkdir(directory, { recursive: true, mode: 0o700 })
  await writeFile(temporaryPath, JSON.stringify(contents, null, 2) + "\n", {
    encoding: "utf8",
    mode: 0o600,
  })
  await rename(temporaryPath, path)
  await chmod(path, 0o600)
}
