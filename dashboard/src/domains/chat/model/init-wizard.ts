import { can, type ProjectRef, type Session } from "@/shared/session"

import type { ChatTranslator } from "./commands"
import { sharedChatT } from "./commands"

/**
 * `/init`, as a state machine rather than as five forms that happen to know
 * about each other.
 *
 * The wizard is a **routed flow**, not a modal: the owner's standing rule is
 * that creating an entity is always its own page, and onboarding a repository
 * creates more entities than any other act in the product — a source
 * connection, a compute binding, two model endpoints and a knowledge index. A
 * dialog for that would be a 26rem box with five screens folded into it and no
 * address you could send anybody.
 *
 * The step lives in the URL as a search parameter, so back works, a link works,
 * and a reload lands where the operator was. What does *not* live in the URL is
 * anything typed: a wizard that put a git remote and a secret reference in the
 * address bar would put both into browser history and into every log the proxy
 * keeps.
 */

export const INIT_STEPS = [
  "repo",
  "compute",
  "models",
  "knowledge",
  "confirm",
] as const

export type InitStep = (typeof INIT_STEPS)[number]

/**
 * The heading each step gets, and the one line under it. The registry holds
 * the EN words; the readers below resolve through the `chat` catalogue with
 * these as `defaultValue` (D11 map-edge rule).
 */
export const STEP_META: Record<InitStep, { title: string; summary: string }> = {
  repo: {
    title: "Repository and git access",
    summary:
      "Where the code lives and what the swarm may do to it. This is the step that decides which project the rest of the wizard is configuring.",
  },
  compute: {
    title: "Compute provider",
    summary:
      "Where worker containers are started, and the ceiling on how many run at once.",
  },
  models: {
    title: "Model endpoints",
    summary:
      "An OpenAI- or Anthropic-compatible base url for the lead model and for the workers. Secrets are named here, never typed here.",
  },
  knowledge: {
    title: "Knowledge",
    summary:
      "Whether this project keeps an indexed rule set, and what the first pass should read.",
  },
  confirm: {
    title: "Confirm",
    summary:
      "What onboarding will do, in the order it will do it. Nothing has been created yet.",
  },
}

/** A step's heading, in the active locale. */
export function stepTitle(
  step: InitStep,
  t: ChatTranslator = sharedChatT
): string {
  return t(`wizard.step.${step}.title`, {
    defaultValue: STEP_META[step].title,
  })
}

/** The one line under a step's heading, in the active locale. */
export function stepSummary(
  step: InitStep,
  t: ChatTranslator = sharedChatT
): string {
  return t(`wizard.step.${step}.summary`, {
    defaultValue: STEP_META[step].summary,
  })
}

/** Everything the wizard collects. One flat object — it is one act. */
export interface InitDraft {
  projectId: string
  remote: string
  branch: string
  writeAccess: boolean
  provider: string
  maxWorkers: string
  leadEndpoint: string
  workerEndpoint: string
  secretRef: string
  knowledge: boolean
  seed: string
}

export const EMPTY_DRAFT: InitDraft = {
  projectId: "",
  remote: "",
  branch: "main",
  writeAccess: false,
  provider: "docker",
  maxWorkers: "8",
  leadEndpoint: "",
  workerEndpoint: "",
  secretRef: "",
  knowledge: true,
  seed: "docs/**, README.md",
}

/** `?step=` as it arrives — a string from the address bar, trusted for nothing. */
export function stepFrom(value: string | undefined): InitStep {
  return INIT_STEPS.includes(value as InitStep) ? (value as InitStep) : "repo"
}

export function stepIndex(step: InitStep): number {
  return INIT_STEPS.indexOf(step)
}

/**
 * Where `/init` may be run.
 *
 * The same rule the composer's scope chip uses, and deliberately the same
 * function's worth of logic: `sources.edit` on the project, asked per project.
 * The first step of this wizard connects a repository, which *is* the sources
 * act — so a shift that administers one project out of three sees one row here
 * and one row in the chip.
 */
export function initProjects(session: Session): ProjectRef[] {
  return session.projects.filter((project) =>
    can(session, "sources.edit", project.id)
  )
}

/**
 * What is missing on this step, if anything.
 *
 * Validation is per step rather than per form, because the operator has to be
 * stopped at the step that can still be fixed — a wizard that collects five
 * screens and then says "the remote is empty" has made the person find the
 * screen again.
 */
export function stepErrors(
  step: InitStep,
  draft: InitDraft,
  t: ChatTranslator = sharedChatT
): Partial<Record<keyof InitDraft, string>> {
  if (step === "repo") {
    const errors: Partial<Record<keyof InitDraft, string>> = {}
    if (!draft.projectId) {
      errors.projectId = t("wizard.error.project")
    }
    if (!draft.remote.trim()) {
      errors.remote = t("wizard.error.remote")
    }
    if (!draft.branch.trim()) {
      errors.branch = t("wizard.error.branch")
    }
    return errors
  }

  if (step === "compute") {
    const workers = Number(draft.maxWorkers)
    if (!Number.isInteger(workers) || workers < 1) {
      return { maxWorkers: t("wizard.error.workers") }
    }
    return {}
  }

  if (step === "models") {
    const errors: Partial<Record<keyof InitDraft, string>> = {}
    if (!draft.leadEndpoint.trim()) {
      errors.leadEndpoint = t("wizard.error.lead")
    }
    if (!draft.secretRef.trim()) {
      errors.secretRef = t("wizard.error.secret")
    }
    return errors
  }

  return {}
}

/**
 * The stages the progress stream reports, in the order they happen. Registry
 * data — the reader below resolves the active locale's word with these as
 * `defaultValue`.
 */
export const INIT_STAGES = [
  "clone the repository",
  "read the client's rules and skills",
  "pull the worker image",
  "reach the model endpoints",
  "seed the knowledge index",
  "register the project with the swarm",
] as const

/** The stage keys, in the same order as `INIT_STAGES`. */
export const INIT_STAGE_KEYS = [
  "clone",
  "rules",
  "image",
  "endpoints",
  "knowledge",
  "register",
] as const

/** A progress-stream stage's label, in the active locale. */
export function initStageLabel(
  index: number,
  t: ChatTranslator = sharedChatT
): string {
  const key = INIT_STAGE_KEYS[index] ?? INIT_STAGE_KEYS[0]
  return t(`wizard.stage.${key}`, {
    defaultValue: INIT_STAGES[index] ?? INIT_STAGES[0],
  })
}
