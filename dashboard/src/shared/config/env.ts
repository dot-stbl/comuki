import { z } from "zod"

/**
 * The platform's own repository.
 *
 * A default rather than a required variable, because the dashboard *ships from*
 * this repository: a fresh checkout should not have to be told its own address
 * before the topbar can point at the source. A fork with a different home
 * overrides it; a deployment with no public source sets it to the empty string,
 * which parses to `null` and takes the link out of the bar entirely rather than
 * leaving an icon that goes nowhere.
 */
const REPO_URL_DEFAULT = "https://github.com/dot-stbl/comuki"

/**
 * A web address, or nothing.
 *
 * Only `http`/`https` pass. The value ends up in an `href` the operator clicks,
 * so a scheme nobody vetted — `javascript:` above all — must not reach the DOM
 * because somebody put it in an env file. Anything else is treated exactly the
 * way an unset variable is: the link does not render.
 */
const WEB_URL = /^https?:\/\/\S+$/

function webUrl(value: string | undefined): string | null {
  const trimmed = value?.trim() ?? ""
  return WEB_URL.test(trimmed) ? trimmed : null
}

const envSchema = z.object({
  /**
   * When true, domain hooks serve shared mock seeds instead of live API.
   *
   * Defaults to `true` when unset or empty. Mock-first is the dashboard's
   * baseline posture: a fresh checkout, a test run, and `bun run dev` all
   * want seed data so screens render without the host running beside them.
   * Real mode is opt-in — the operator sets `VITE_USE_MOCK=false` (and
   * `VITE_API_BASE_URL=...`) to point at the host. The transform accepts
   * the empty/undefined case as a request to mock, not as "I forgot".
   */
  VITE_USE_MOCK: z
    .enum(["true", "false", "1", "0", ""])
    .optional()
    .transform(
      (value) =>
        value === undefined || value === "" || value === "true" || value === "1",
    ),
  /** Where the platform's source lives — see `REPO_URL_DEFAULT`. */
  VITE_REPO_URL: z.string().optional(),
  /**
   * The commit SHA this build was assembled from. CI writes it into the
   * bundle; local `bun run dev` reads `''` and the footer renders the empty
   * slot gracefully. Short enough to copy into a ticket — eight characters
   * is what `git rev-parse --short` hands out by default.
   */
  VITE_COMMIT_SHA: z.string().optional(),
  /**
   * Where this build is meant to run. Whatever the variable says is the
   * label; no inference from hostname or `import.meta.env.DEV`, because a
   * build meant for staging looks the same as a build meant for production
   * to the bundler, and operators typing their real password into the wrong
   * environment is the one class of bug a label exists to prevent.
   */
  VITE_DEPLOY_ENV: z.enum(["local", "staging", "production"]).optional(),
  /**
   * The host URL kubb-generated clients route through. Read directly by the
   * kubb-client transport (which throws a single, helpful error when the
   * variable is missing); exposed here so domain code can branch on "is the
   * operator pointed at a real backend yet?". Empty when unset.
   *
   * The contract on this is "non-empty" in real mode (kubb-client refuses
   * empty); the schema keeps it `optional()` so mock-first setups parse —
   * the mock gate (`useMock`) decides whether the value is read at all.
   */
  VITE_API_BASE_URL: z.string().optional(),
  /**
   * The name of the OIDC provider the host is configured for, e.g. `comuki`.
   * Drives the "Continue with …" button and the `/api/v1/auth/oidc/{name}/start`
   * navigation in real mode. Optional in mock mode (the mock store is the
   * source of truth there); in real mode the absence of a configured
   * provider hides the button entirely.
   *
   * Runtime provider discovery would be cleaner (`GET /api/v1/auth/oidc/providers`)
   * but is out of scope for this slice and the host has no such endpoint today.
   */
  VITE_OIDC_PROVIDER: z.string().optional(),
})

const parsed = envSchema.parse({
  VITE_USE_MOCK: import.meta.env.VITE_USE_MOCK ?? "",
  // `??`, not `||`: an author who writes `VITE_REPO_URL=` is asking for no
  // link, and that is a different answer from not having written the line.
  VITE_REPO_URL: import.meta.env.VITE_REPO_URL ?? REPO_URL_DEFAULT,
  VITE_COMMIT_SHA: import.meta.env.VITE_COMMIT_SHA ?? "",
  VITE_DEPLOY_ENV: import.meta.env.VITE_DEPLOY_ENV ?? "local",
  VITE_API_BASE_URL: import.meta.env.VITE_API_BASE_URL ?? "",
  VITE_OIDC_PROVIDER: import.meta.env.VITE_OIDC_PROVIDER ?? "",
})

export const env = {
  useMock: parsed.VITE_USE_MOCK,
  /** The repository link's destination, or `null` when the bar shows none. */
  repoUrl: webUrl(parsed.VITE_REPO_URL),
  /** The commit SHA the bundle was assembled from — empty when not set. */
  commitSha: (parsed.VITE_COMMIT_SHA ?? "").trim().slice(0, 8),
  /** Where this build is meant to run — `local` if `VITE_DEPLOY_ENV` is unset. */
  deployEnv: parsed.VITE_DEPLOY_ENV,
  /**
   * Host URL kubb-generated clients route through. Empty when unset;
   * combined with `useMock=false`, kubb-client throws at first call rather
   * than pinging localhost and returning a Vite-served 404.
   */
  apiBaseUrl: (parsed.VITE_API_BASE_URL ?? "").trim(),
  /**
   * Configured OIDC provider name, or `null` when unset. In mock mode this is
   * informational only — `auth.store` owns the button — but in real mode it
   * is the one fact the SPA has about whether an identity provider is wired.
   */
  oidcProvider: (parsed.VITE_OIDC_PROVIDER ?? "").trim() || null,
}

export type Env = typeof env
