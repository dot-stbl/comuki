/**
 * Auth helpers: the `comuki login` flow (email+password → session cookie →
 * config file) and the credential summary the header line prints. v1 is
 * deliberately simple: API key or cookie, no OIDC.
 */
import {
  ComukiClient,
  ComukiApiError,
  type LoginSuccess,
  type MeView,
} from "./client"
import { colors, symbols } from "../theme"
import {
  configFilePath,
  readConfigFile,
  resolveConfig,
  writeConfigFile,
} from "./config"

/** POSTs credentials and persists the returned session cookie. */
export async function loginAndStore(
  url: string | undefined,
  email: string,
  password: string
): Promise<LoginSuccess> {
  // A cookie-less client for the login call itself.
  const bootstrap = new ComukiClient(
    resolveConfig({ COMUKI_URL: url }, {}, { url })
  )
  const success = await bootstrap.login(email, password)

  const existing = await readConfigFile()
  await writeConfigFile({
    ...existing,
    url: url ?? existing.url,
    cookie: success.cookie,
  })
  return success
}

/** The header-line identity: display name / email / api key subject. */
export interface WhoAmI {
  readonly kind: "user" | "apiKey" | "anonymous"
  readonly label: string
}

/** Maps a `/auth/me` payload to the header-line identity. */
export function whoFromMe(me: MeView): WhoAmI {
  if (me.subjectType === "api-key") {
    return { kind: "apiKey", label: `api key ${shortId(me.subjectId)}` }
  }
  return {
    kind: "user",
    label: me.displayName ?? me.email ?? `user ${shortId(me.subjectId)}`,
  }
}

/**
 * Transcript / `comuki whoami` lines: kind · label, then roles and
 * permissions when the payload carried them.
 */
export function formatWhoamiLines(
  who: WhoAmI,
  me?: MeView
): readonly string[] {
  const lines = [
    `${colors.accent}  ${who.kind}${colors.reset} ${symbols.bullet} ${who.label}`,
  ]
  if (me !== undefined && me.roles.length > 0) {
    lines.push(`${colors.dim}  roles: ${me.roles.join(", ")}${colors.reset}`)
  }
  if (me !== undefined && me.permissions.length > 0) {
    lines.push(
      `${colors.dim}  permissions: ${me.permissions.join(", ")}${colors.reset}`
    )
  }
  return lines
}

/** 401 → anonymous; any other failure → offline. */
export function whoFromError(error: unknown): WhoAmI {
  if (error instanceof ComukiApiError && error.status === 401) {
    return { kind: "anonymous", label: "anonymous" }
  }
  return { kind: "anonymous", label: "offline" }
}

export async function whoAmI(client: ComukiClient): Promise<WhoAmI> {
  try {
    return whoFromMe(await client.me())
  } catch (error) {
    return whoFromError(error)
  }
}

export function shortId(id: string): string {
  return id.slice(0, 8)
}

/**
 * REPL-facing copy for mid-session auth failures. Null when the error
 * is not an auth problem — the caller falls back to `describeError`.
 *
 * 401 = the cookie died (sliding refresh never saved, or the host
 * recycled data-protection keys). 403 with an API key = the known
 * "keys have no roles" gap — do not retry the key.
 */
export function describeAuthFailure(
  error: unknown,
  usingApiKey: boolean
): string | null {
  if (!(error instanceof ComukiApiError)) {
    return null
  }
  if (error.status === 401) {
    return "session expired — run /login to sign in again"
  }
  if (error.status === 403 && usingApiKey) {
    return "API keys don't carry roles yet — run /login (cookie) or grant the key platform-admin"
  }
  return null
}

export { configFilePath }
