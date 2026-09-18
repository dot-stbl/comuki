/**
 * `comuki setup` — the interactive first-run wizard. Four steps in one
 * Ink app: server URL (validated + probed against /api/v1/health,
 * unreachable degrades to "save anyway?"), auth (login / paste api
 * key / skip), default project (picked from the host when
 * authenticated, free text otherwise) and theme (7 palettes with a
 * live accent/dim/ok swatch preview, then dark|light). The result is
 * written to `~/.config/comuki/config.json` in one shot — secrets are
 * never echoed back (the summary masks the key to 8 chars, the cookie
 * to its name). Esc at any step aborts with a dim notice and saves
 * nothing.
 *
 * The step validators, the health probe, the config merge and the
 * summary are pure/exported — the wizard UI is a thin shell over
 * them, and the tests never mount Ink.
 */
import { Box, Text, useApp, useInput } from "ink"
import TextInput from "ink-text-input"
import React, { useEffect, useState, type ReactNode } from "react"
import { ComukiClient, type ProjectView } from "../lib/client"
import {
  configFilePath,
  readConfigFile,
  writeConfigFile,
  type ConfigFileContents,
} from "../lib/config"
import { CLI_THEMES, type CliTheme, type ThemeMode } from "../themes"
import { colors, palette, symbols } from "../theme"
import { describeError } from "./chat"
import { maskApiKey, maskCookie } from "./config"

// ---------------------------------------------------------------------------
// Pure step logic (exported for tests)
// ---------------------------------------------------------------------------

/** Injectable transport for the health probe (tests fake it). */
export type ProbeFetch = (
  input: string,
  init?: RequestInit
) => Promise<Response>

/** `http(s)://host[:port]` — a parseable URL with a host and nothing stranger. */
export function isValidServerUrl(value: string): boolean {
  const trimmed = value.trim()
  if (!/^https?:\/\//.test(trimmed)) {
    return false
  }
  try {
    return new URL(trimmed).hostname.length > 0
  } catch {
    return false
  }
}

/**
 * `ck_…` mask-check: the `ck_` prefix plus a base64url body long
 * enough to be a real key (prefix + secret, 8 + 43 on the host today).
 * Deliberately not the full length contract — the server is the judge.
 */
export function isValidApiKeyFormat(value: string): boolean {
  return /^ck_[A-Za-z0-9_-]{16,}$/.test(value.trim())
}

/** Trims and drops trailing slashes — the same shape `resolveConfig` stores. */
export function normalizeServerUrl(value: string): string {
  return value.trim().replace(/\/+$/, "")
}

/** GET `<url>/api/v1/health` — any 2xx counts as reachable. */
export async function probeHealth(
  url: string,
  fetchImpl: ProbeFetch = fetch,
  timeoutMs = 4000
): Promise<boolean> {
  try {
    const response = await fetchImpl(
      `${normalizeServerUrl(url)}/api/v1/health`,
      { signal: AbortSignal.timeout(timeoutMs) }
    )
    return response.ok
  } catch {
    return false
  }
}

/** What the wizard collected by the end — one field per step answered. */
export interface SetupDraft {
  readonly url: string
  readonly cookie?: string
  readonly apiKey?: string
  readonly defaultProject?: string
  readonly theme: string
}

/** Merges the draft into the on-disk file without touching unknown keys. */
export function buildSetupFileContents(
  existing: ConfigFileContents,
  draft: SetupDraft
): ConfigFileContents {
  return {
    ...existing,
    url: draft.url,
    ...(draft.cookie ? { cookie: draft.cookie } : {}),
    ...(draft.apiKey ? { apiKey: draft.apiKey } : {}),
    ...(draft.defaultProject ? { defaultProject: draft.defaultProject } : {}),
    theme: draft.theme,
  }
}

/** The final screen, credentials masked — secrets never print in full. */
export function formatSetupSummary(
  draft: SetupDraft,
  configPath: string
): string {
  const auth = draft.apiKey
    ? `api-key ${maskApiKey(draft.apiKey)}`
    : draft.cookie
      ? `cookie ${maskCookie(draft.cookie)}`
      : "anonymous"
  return [
    `${colors.ok}${symbols.checkmark}${colors.reset} setup complete — config saved to ${configPath}`,
    `  url      ${draft.url}`,
    `  auth     ${auth}`,
    `  project  ${draft.defaultProject ?? "—"}`,
    `  theme    ${draft.theme}`,
  ].join("\n")
}

// ---------------------------------------------------------------------------
// Wizard UI
// ---------------------------------------------------------------------------

export interface SetupAppProps {
  /** Test seam for the health probe and login/project calls. */
  readonly fetchImpl?: ProbeFetch
}

type SetupStep =
  | "url"
  | "urlProbe"
  | "urlConfirm"
  | "auth"
  | "loginEmail"
  | "loginPassword"
  | "authWorking"
  | "apiKey"
  | "projectLoad"
  | "project"
  | "projectText"
  | "theme"
  | "themeMode"
  | "saving"
  | "done"
  | "aborted"

const AUTH_OPTIONS = [
  { key: "login", label: "login with email + password" },
  { key: "key", label: "paste an api key (ck_…)" },
  { key: "skip", label: "skip — configure auth later" },
] as const

const PROJECT_OTHER = "__other__"
const PROJECT_SKIP = "__skip__"

/**
 * Arrow-key picker: ↑/↓ move, enter picks. Escape is NOT handled here
 * — the app-level hook owns the global "esc aborts the wizard" rule so
 * it fires from every step, inputs included.
 */
function Menu({
  rows,
  onPick,
  children,
}: {
  readonly rows: readonly { readonly key: string; readonly label: string }[]
  readonly onPick: (key: string, index: number) => void
  readonly children?: (selected: number) => ReactNode
}) {
  const [selected, setSelected] = useState(0)
  useInput((input, key) => {
    if (key.upArrow) {
      setSelected((current) => (current - 1 + rows.length) % rows.length)
      return
    }
    if (key.downArrow) {
      setSelected((current) => (current + 1) % rows.length)
      return
    }
    if (key.return) {
      const row = rows[selected]
      if (row) {
        onPick(row.key, selected)
      }
    }
  })
  return (
    <Box flexDirection="column">
      {rows.map((row, index) => (
        <Text key={row.key}>
          {"  "}
          <Text
            color={index === selected ? palette.brand : undefined}
            dimColor={index !== selected}
          >
            {index === selected ? "› " : "  "}
            {row.label}
          </Text>
        </Text>
      ))}
      {children ? <Box marginTop={1}>{children(selected)}</Box> : null}
    </Box>
  )
}

/** One reading of one palette: each word in its own hex — colour never rides alone. */
function SwatchLine({
  theme,
  mode,
}: {
  readonly theme: CliTheme
  readonly mode: ThemeMode
}) {
  const primitives = mode === "dark" ? theme.dark : theme.light
  return (
    <Text>
      {"  "}
      <Text dimColor>{mode.padEnd(5)}</Text>
      {"  "}
      <Text color={primitives.running}>{`${symbols.brandMark} accent`}</Text>
      <Text dimColor>{" · "}</Text>
      <Text color={primitives.muted}>dim</Text>
      <Text dimColor>{" · "}</Text>
      <Text color={primitives.success}>ok</Text>
      <Text dimColor>{` · ${primitives.running} ${primitives.muted} ${primitives.success}`}</Text>
    </Text>
  )
}

/** `  label › ` prompt prefix shared by every text-input step. */
function Prompt({ label }: { readonly label: string }) {
  return (
    <Text color={palette.brand}>{`  ${label.padEnd(8)}${symbols.prompt} `}</Text>
  )
}

/** The notice line's two voices: validation failure vs best-effort info. */
type Notice = { readonly text: string; readonly tone: "error" | "info" }

export function SetupApp({ fetchImpl }: SetupAppProps) {
  const { exit } = useApp()
  const [step, setStep] = useState<SetupStep>("url")
  const [notice, setNotice] = useState<Notice | null>(null)

  const [urlInput, setUrlInput] = useState("")
  const [draftUrl, setDraftUrl] = useState("")
  const [email, setEmail] = useState("")
  const [password, setPassword] = useState("")
  const [apiKeyInput, setApiKeyInput] = useState("")
  const [cookie, setCookie] = useState<string | undefined>(undefined)
  const [apiKey, setApiKey] = useState<string | undefined>(undefined)
  const [projects, setProjects] = useState<readonly ProjectView[] | null>(null)
  const [defaultProject, setDefaultProject] = useState<string | undefined>(
    undefined
  )
  const [chosenTheme, setChosenTheme] = useState<CliTheme>(CLI_THEMES[0])
  const [chosenMode, setChosenMode] = useState<ThemeMode>("dark")
  const [summary, setSummary] = useState("")

  // Prefill the url from an existing config (re-runs of the wizard).
  useEffect(() => {
    void (async () => {
      const existing = await readConfigFile()
      if (existing.url) {
        setUrlInput(existing.url)
      }
      if (existing.theme) {
        const match = CLI_THEMES.find((theme) =>
          existing.theme?.startsWith(`${theme.id}-`)
        )
        if (match) {
          setChosenTheme(match)
          setChosenMode(existing.theme.endsWith("-light") ? "light" : "dark")
        }
      }
    })()
  }, [])

  useEffect(() => {
    if (step === "done" || step === "aborted") {
      exit()
    }
  }, [step, exit])

  const abort = () => {
    setStep("aborted")
  }

  // Global keys: esc aborts from any step except the terminal ones; the
  // unreachable-url confirmation also answers y/n here (no Menu mounted).
  const keysActive = step !== "done" && step !== "aborted" && step !== "saving"
  useInput(
    (input, key) => {
      if (key.escape) {
        abort()
        return
      }
      if (step === "urlConfirm") {
        if (input === "y" || input === "Y") {
          setStep("auth")
        }
        if (input === "n" || input === "N") {
          setStep("url")
        }
      }
    },
    { isActive: keysActive }
  )

  const submitUrl = (value: string) => {
    if (!isValidServerUrl(value)) {
      setNotice({ text: "url must start with http:// or https://", tone: "error" })
      return
    }
    setNotice(null)
    const normalized = normalizeServerUrl(value)
    setDraftUrl(normalized)
    setStep("urlProbe")
    void (async () => {
      const reachable = await probeHealth(normalized, fetchImpl)
      if (reachable) {
        setStep("auth")
      } else {
        setStep("urlConfirm")
      }
    })()
  }

  const clientForDraft = () =>
    new ComukiClient(
      { url: draftUrl, apiKey, cookie, bell: true },
      fetchImpl ? { fetchImpl } : {}
    )

  const submitPassword = (value: string) => {
    setStep("authWorking")
    void (async () => {
      try {
        const bootstrap = new ComukiClient(
          { url: draftUrl, bell: true },
          fetchImpl ? { fetchImpl } : {}
        )
        const success = await bootstrap.login(email.trim(), value)
        setCookie(success.cookie)
        setStep("projectLoad")
      } catch (error) {
        setNotice({ text: describeError(error), tone: "error" })
        setStep("auth")
      }
    })()
  }

  const submitApiKey = (value: string) => {
    if (!isValidApiKeyFormat(value)) {
      setNotice({
        text: "that does not look like a comuki api key — expected ck_…",
        tone: "error",
      })
      return
    }
    setNotice(null)
    setApiKey(value.trim())
    setStep("projectLoad")
  }

  // Authenticated → ask the host for the project list; offline/401 → free text.
  useEffect(() => {
    if (step !== "projectLoad") {
      return
    }
    void (async () => {
      try {
        const list = await clientForDraft().projects()
        setProjects(list)
        setStep("project")
      } catch {
        setProjects(null)
        setNotice({
          text: "project list unavailable — type a project id, slug or name",
          tone: "info",
        })
        setStep("projectText")
      }
    })()
  }, [step])

  const submitProjectText = (value: string) => {
    const trimmed = value.trim()
    if (trimmed.length > 0) {
      setDefaultProject(trimmed)
    }
    setNotice(null)
    setStep("theme")
  }

  const finish = (theme: CliTheme, mode: ThemeMode) => {
    const choice = `${theme.id}-${mode}`
    setStep("saving")
    void (async () => {
      try {
        const next = buildSetupFileContents(await readConfigFile(), {
          url: draftUrl,
          cookie,
          apiKey,
          defaultProject,
          theme: choice,
        })
        await writeConfigFile(next)
        setSummary(
          formatSetupSummary(
            { url: draftUrl, cookie, apiKey, defaultProject, theme: choice },
            configFilePath()
          )
        )
        setStep("done")
      } catch (error) {
        // A failed write saves nothing — back to the last step to retry.
        setNotice({ text: describeError(error), tone: "error" })
        setStep("themeMode")
      }
    })()
  }

  if (step === "done") {
    return <Text>{summary}</Text>
  }
  if (step === "aborted") {
    return (
      <Text dimColor>{`${colors.faint}  setup skipped — nothing saved${colors.reset}`}</Text>
    )
  }

  const header = (
    <Text>
      <Text color={palette.brand}>{`${symbols.brandMark} comuki setup`}</Text>
      <Text dimColor>
        {"  — esc skips, nothing is saved until the last step"}
      </Text>
    </Text>
  )
  const noticeLine = notice ? (
    <Text>
      {"  "}
      {notice.tone === "error"
        ? `${colors.error}${symbols.cross}${colors.reset} ${notice.text}`
        : `${colors.faint}${notice.text}${colors.reset}`}
    </Text>
  ) : null

  return (
    <Box flexDirection="column" gap={1}>
      {header}
      {noticeLine}

      {step === "url" ? (
        <Text>
          <Prompt label="url" />
          <TextInput
            value={urlInput}
            onChange={(next) => {
              setNotice(null)
              setUrlInput(next)
            }}
            onSubmit={submitUrl}
          />
        </Text>
      ) : null}

      {step === "urlProbe" ? (
        <Text dimColor>{`  … reaching ${draftUrl}/api/v1/health`}</Text>
      ) : null}

      {step === "urlConfirm" ? (
        <Text>
          {"  "}
          <Text color={palette.waiting}>
            {`${symbols.bullet} ${draftUrl} unreachable`}
          </Text>
          <Text dimColor>{"  save anyway? y/n"}</Text>
        </Text>
      ) : null}

      {step === "auth" ? (
        <Menu
          rows={AUTH_OPTIONS.map((option) => ({
            key: option.key,
            label: option.label,
          }))}
          onPick={(key) => {
            setNotice(null)
            if (key === "login") {
              setStep("loginEmail")
            }
            if (key === "key") {
              setStep("apiKey")
            }
            if (key === "skip") {
              setStep("projectText")
            }
          }}
        />
      ) : null}

      {step === "loginEmail" ? (
        <Text>
          <Prompt label="email" />
          <TextInput
            value={email}
            onChange={setEmail}
            onSubmit={(submitted) => {
              if (submitted.trim().length > 0) {
                setStep("loginPassword")
              }
            }}
          />
        </Text>
      ) : null}

      {step === "loginPassword" ? (
        <Text>
          <Prompt label="pass" />
          <TextInput
            value={password}
            onChange={setPassword}
            mask="*"
            onSubmit={submitPassword}
          />
        </Text>
      ) : null}

      {step === "authWorking" ? (
        <Text dimColor>{"  … signing in"}</Text>
      ) : null}

      {step === "apiKey" ? (
        <Text>
          <Prompt label="api key" />
          <TextInput
            value={apiKeyInput}
            onChange={setApiKeyInput}
            mask="*"
            onSubmit={submitApiKey}
          />
        </Text>
      ) : null}

      {step === "projectLoad" ? (
        <Text dimColor>{"  … fetching projects"}</Text>
      ) : null}

      {step === "project" && projects ? (
        <Menu
          rows={[
            ...projects.map((project) => ({
              key: project.slug,
              label: `${project.slug} — ${project.name}`,
            })),
            { key: PROJECT_OTHER, label: "type another project…" },
            { key: PROJECT_SKIP, label: "skip — no default project" },
          ]}
          onPick={(key) => {
            if (key === PROJECT_OTHER) {
              setNotice(null)
              setStep("projectText")
              return
            }
            if (key !== PROJECT_SKIP) {
              setDefaultProject(key)
            }
            setStep("theme")
          }}
        />
      ) : null}

      {step === "projectText" ? (
        <Text>
          <Prompt label="project" />
          <TextInput
            value={defaultProject ?? ""}
            onChange={(next) => setDefaultProject(next)}
            onSubmit={submitProjectText}
          />
          <Text dimColor>{"  (empty = skip)"}</Text>
        </Text>
      ) : null}

      {step === "theme" ? (
        <Menu
          rows={CLI_THEMES.map((theme) => ({
            key: theme.id,
            label: theme.name,
          }))}
          onPick={(key, index) => {
            const theme = CLI_THEMES[index]
            if (theme) {
              setChosenTheme(theme)
              setStep("themeMode")
            }
          }}
        >
          {(selected) => {
            const theme = CLI_THEMES[selected] ?? chosenTheme
            return (
              <Box flexDirection="column">
                <SwatchLine theme={theme} mode="dark" />
                <SwatchLine theme={theme} mode="light" />
              </Box>
            )
          }}
        </Menu>
      ) : null}

      {step === "themeMode" ? (
        <Menu
          rows={[
            { key: "dark", label: "dark" },
            { key: "light", label: "light" },
          ]}
          onPick={(key) => {
            const mode = key === "light" ? "light" : "dark"
            setChosenMode(mode)
            finish(chosenTheme, mode)
          }}
        >
          {() => (
            <Box flexDirection="column">
              <SwatchLine theme={chosenTheme} mode="dark" />
              <SwatchLine theme={chosenTheme} mode="light" />
            </Box>
          )}
        </Menu>
      ) : null}

      {step === "saving" ? (
        <Text dimColor>{"  … writing ~/.config/comuki/config.json"}</Text>
      ) : null}
    </Box>
  )
}
