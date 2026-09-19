/**
 * `comuki --tui opentui` — boot the focus-mode OpenTUI Core host
 * (issue #73) on the ClientKernel with REAL ports.
 *
 * Composition only (the ADR-0002 host lives in `../tui/`): resolve
 * config → client → SignalR transport + HTTP ports + JSON workspace →
 * production renderer (alternate-screen) → TUI host → kernel.start().
 * The default (no flag) stays on Ink — this path is opt-in.
 *
 * Clean-exit contract: the exit command / ctrl+c runs the host's
 * `close()` — kernel.stop() → whenIdle() → renderer.destroy() — and
 * only then the process exits with the terminal restored.
 */

import {
  CliRenderEvents,
  createCliRenderer,
} from "@opentui/core"
import { createClientKernel } from "../kernel"
import { projectId as toProjectId } from "../harness/state"
import {
  HttpApprovalPort,
  HttpConversationPort,
  JsonWorkspaceStore,
} from "../kernel/adapters/http"
import { SignalRKernelTransport } from "../kernel/adapters/signalr"
import { ComukiClient } from "../lib/client"
import type { ResolvedConfig } from "../lib/config"
import { sessionsFilePath } from "../lib/config"
import { DEFAULT_LOCALE, type LocaleCode } from "../locales"
import { createTuiHost } from "../tui/host"

function localeFromEnv(): LocaleCode {
  return process.env.COMUKI_LANG === "ru" ? "ru" : DEFAULT_LOCALE
}

/** Best-effort project resolution (id/slug/name) — mirrors the Ink host. */
async function resolveProjectId(
  client: ComukiClient,
  wanted: string | undefined
): Promise<string | undefined> {
  if (!wanted) {
    return undefined
  }
  try {
    const projects = await client.projects()
    const match = projects.find(
      (candidate) =>
        candidate.id === wanted ||
        candidate.slug === wanted ||
        candidate.name === wanted
    )
    return match?.id
  } catch {
    return undefined
  }
}

export async function runOpentuiRepl(
  config: ResolvedConfig,
  project?: string
): Promise<void> {
  const client = new ComukiClient(config)
  const projectId = await resolveProjectId(client, project ?? config.defaultProject)

  const transport = new SignalRKernelTransport({
    hubUrl: client.hubUrl(),
    headers: client.hubHeaders(),
  })
  const kernel = createClientKernel({
    ports: {
      // The getter picks up any future credential swap on the client.
      conversation: new HttpConversationPort(() => client),
      approval: new HttpApprovalPort(() => client),
      realtime: transport,
      workspace: new JsonWorkspaceStore(sessionsFilePath()),
    },
    feed: transport,
  })

  const renderer = await createCliRenderer({
    screenMode: "alternate-screen",
    // ctrl+c belongs to the exit command so the kernel-stop →
    // whenIdle → renderer.destroy ordering always holds.
    exitOnCtrlC: false,
    useMouse: false,
  })
  const width = renderer.width > 0 ? renderer.width : process.stdout.columns ?? 80
  const height = renderer.height > 0 ? renderer.height : process.stdout.rows ?? 24

  let exited = false
  const host = await createTuiHost(kernel, {
    renderer,
    width,
    height,
    locale: localeFromEnv(),
    newSessionProjectId: projectId !== undefined ? toProjectId(projectId) : null,
    onExit: () => {
      exited = true
      // renderer.destroy() has already restored the terminal (close()
      // runs it before onExit); a hard exit avoids lingering handles.
      process.exit(0)
    },
  })

  const onResize = () => {
    void host.setSize(
      renderer.width > 0 ? renderer.width : width,
      renderer.height > 0 ? renderer.height : height
    )
  }
  renderer.on(CliRenderEvents.RESIZE, onResize)

  kernel.start()

  try {
    // Hold the process open until the exit command shuts everything
    // down; if the renderer dies first (stdout closed), fall through
    // to the same teardown.
    await new Promise<void>((resolve) => {
      renderer.once(CliRenderEvents.DESTROY, () => resolve())
      const poll = setInterval(() => {
        if (exited) {
          clearInterval(poll)
          resolve()
        }
      }, 250)
    })
  } finally {
    renderer.off(CliRenderEvents.RESIZE, onResize)
    await host.close()
  }
}
