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
  /** When true, domain hooks serve shared mock seeds instead of live API. */
  VITE_USE_MOCK: z
    .enum(["true", "false", "1", "0", ""])
    .optional()
    .transform((value) => value === "true" || value === "1"),
  /** Where the platform's source lives — see `REPO_URL_DEFAULT`. */
  VITE_REPO_URL: z.string().optional(),
})

const parsed = envSchema.parse({
  VITE_USE_MOCK: import.meta.env.VITE_USE_MOCK ?? "",
  // `??`, not `||`: an author who writes `VITE_REPO_URL=` is asking for no
  // link, and that is a different answer from not having written the line.
  VITE_REPO_URL: import.meta.env.VITE_REPO_URL ?? REPO_URL_DEFAULT,
})

export const env = {
  useMock: parsed.VITE_USE_MOCK,
  /** The repository link's destination, or `null` when the bar shows none. */
  repoUrl: webUrl(parsed.VITE_REPO_URL),
}

export type Env = typeof env
