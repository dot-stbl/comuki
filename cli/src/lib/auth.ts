/**
 * Auth helpers: the `comuki login` flow (email+password → session cookie →
 * config file) and the credential summary the header line prints. v1 is
 * deliberately simple: API key or cookie, no OIDC.
 */
import { ComukiClient, ComukiApiError, type LoginSuccess } from "./client"
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

export async function whoAmI(client: ComukiClient): Promise<WhoAmI> {
  try {
    const me = await client.me()
    if (me.subjectType === "api-key") {
      return { kind: "apiKey", label: `api key ${shortId(me.subjectId)}` }
    }
    return {
      kind: "user",
      label: me.displayName ?? me.email ?? `user ${shortId(me.subjectId)}`,
    }
  } catch (error) {
    if (error instanceof ComukiApiError && error.status === 401) {
      return { kind: "anonymous", label: "anonymous" }
    }
    // Server unreachable — the command surfaces the real error separately.
    return { kind: "anonymous", label: "offline" }
  }
}

export function shortId(id: string): string {
  return id.slice(0, 8)
}

export { configFilePath }
