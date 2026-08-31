import { useCallback, useEffect, useMemo, useState } from "react"
import { useNavigate, useRouter } from "@tanstack/react-router"

import {
  FormActions,
  FormFields,
  FormLayout,
  FormPage,
  FormRow,
} from "@/app/layout/form-page"
import {
  EMPTY_DRAFT,
  INIT_STAGES,
  INIT_STEPS,
  STEP_META,
  initProjects,
  stepErrors,
  stepIndex,
  type InitDraft,
  type InitStep,
} from "@/domains/chat/model/init-wizard"
import { cn } from "@/shared/lib/utils"
import { useCan, useSession } from "@/shared/session"
import {
  Button,
  Notice,
  SelectField,
  StatusBadge,
  SwitchField,
  TextField,
  TextareaField,
} from "@/shared/ui"

import styles from "./init-wizard-page.module.css"

/** How long one onboarding stage takes to report, in the mock. */
const STAGE_MS = 900

export interface InitWizardPageProps {
  step: InitStep
  /** The project the console scoped `/init` to, when it was launched from chat. */
  project?: string
}

/**
 * `/init` — onboarding a repository, as its own screen.
 *
 * Five steps and then a progress stream. It is a route rather than a modal
 * because creating an entity is always its own page here, and because a wizard
 * needs three things a dialog cannot give it: a path back, an address to send
 * somebody, and room.
 *
 * The step is a search parameter, so back and reload both work. What was typed
 * is component state and stays out of the address bar — a git remote and a
 * secret reference in a URL are a git remote and a secret reference in browser
 * history.
 *
 * ## What is left for a follow-up
 *
 * The four collecting steps ask the fields §7 names and validate them per step
 * — which is the part that decides whether the flow *works*. What they do not
 * yet do is talk to anything: there is no repository reachability check on the
 * remote, no provider probe on the compute step, and no `GET /models` against
 * the endpoints, so a wrong value is caught at the confirm stream rather than
 * in the field that holds it. The progress stream is scripted from
 * `INIT_STAGES` and reports success on every stage; the shapes for a failed
 * stage and for a retry are in place (`data-stage`, the badge, the notice) and
 * unused.
 */
export function InitWizardPage({ step, project }: InitWizardPageProps) {
  const session = useSession()
  const navigate = useNavigate()
  const router = useRouter()

  const projects = useMemo(() => initProjects(session), [session])
  const [draft, setDraft] = useState<InitDraft>(() => ({
    ...EMPTY_DRAFT,
    projectId:
      project && projects.some((entry) => entry.id === project) ? project : "",
  }))
  const [showErrors, setShowErrors] = useState(false)
  const [running, setRunning] = useState(false)
  const [stage, setStage] = useState(0)

  const errors = stepErrors(step, draft)
  const shown = showErrors ? errors : {}
  const index = stepIndex(step)
  const last = index === INIT_STEPS.length - 1

  // Asked on the chosen project, not on the platform: the wizard's own step
  // list is already filtered, so this can only refuse when the address bar
  // carried a project the session may not touch.
  const allowed = useCan("sources.edit", draft.projectId || undefined)

  const set = useCallback(
    <K extends keyof InitDraft>(key: K, value: InitDraft[K]) => {
      setDraft((current) => ({ ...current, [key]: value }))
      setShowErrors(false)
    },
    []
  )

  const goto = useCallback(
    (next: InitStep) => {
      setShowErrors(false)
      void navigate({ to: "/chat/init", search: { step: next }, replace: true })
    },
    [navigate]
  )

  /* The stream, advanced on an interval and stopped on unmount. Deliberately
     not an animation — it is a sequence of facts arriving, and it reports the
     same way with motion turned off. */
  useEffect(() => {
    if (!running || stage >= INIT_STAGES.length) {
      return
    }
    const timer = window.setTimeout(
      () => setStage((current) => current + 1),
      STAGE_MS
    )
    return () => window.clearTimeout(timer)
  }, [running, stage])

  const onSubmit = () => {
    if (Object.keys(errors).length > 0) {
      setShowErrors(true)
      return
    }
    if (!last) {
      goto(INIT_STEPS[index + 1] as InitStep)
      return
    }
    if (allowed.denial) {
      return
    }
    setRunning(true)
    setStage(1)
  }

  const back = () => {
    if (index > 0) {
      goto(INIT_STEPS[index - 1] as InitStep)
      return
    }
    if (router.history.canGoBack()) {
      router.history.back()
      return
    }
    void navigate({ to: "/chat" })
  }

  const meta = STEP_META[step]

  return (
    <FormPage
      title="Onboard a repository"
      crumbs={[
        { label: "console", to: "/chat" },
        { label: "onboard a repository" },
      ]}
      summary={running ? "Onboarding is running." : meta.summary}
    >
      <ol className={styles.steps} data-test="init-steps">
        {INIT_STEPS.map((entry, at) => (
          <li
            key={entry}
            className={cn(
              styles.step,
              at === index && !running && styles.stepCurrent,
              (at < index || running) && styles.stepDone
            )}
            aria-current={at === index && !running ? "step" : undefined}
            data-test="init-step"
            data-step={entry}
          >
            <span className={styles.stepNumber}>{at + 1}</span>
            <span className={styles.stepName}>{STEP_META[entry].title}</span>
          </li>
        ))}
      </ol>

      {running ? (
        <section className={styles.stream} data-test="init-stream">
          <h2 className={styles.streamHead}>onboarding</h2>
          <ol className={styles.stages}>
            {INIT_STAGES.map((label, at) => (
              <li
                key={label}
                className={styles.stage}
                data-test="init-stage"
                data-stage={
                  at < stage ? "success" : at === stage ? "running" : "queued"
                }
              >
                <StatusBadge
                  status={
                    at < stage ? "success" : at === stage ? "running" : "queued"
                  }
                  size="sm"
                >
                  {at < stage ? "done" : at === stage ? "running" : "queued"}
                </StatusBadge>
                <span className={styles.stageLabel}>{label}</span>
              </li>
            ))}
          </ol>
          {stage >= INIT_STAGES.length ? (
            <>
              <Notice tone="ok" data-test="init-done">
                The project is registered. Its rule set, its worker image and
                its endpoints are what the swarm will use from the next run on.
              </Notice>
              <FormActions>
                <Button
                  onClick={() => {
                    void navigate({ to: "/chat" })
                  }}
                >
                  Back to the console
                </Button>
              </FormActions>
            </>
          ) : null}
        </section>
      ) : (
        <FormLayout
          data-test="init-form"
          onSubmit={(event) => {
            event.preventDefault()
            onSubmit()
          }}
        >
          <FormFields>
            {step === "repo" ? (
              <>
                <SelectField
                  id="init-project"
                  label="Project"
                  value={draft.projectId}
                  onValueChange={(next) => set("projectId", next)}
                  options={projects.map((entry) => ({
                    value: entry.id,
                    label: entry.key,
                  }))}
                  placeholder="pick a project"
                  hint="Only the projects where you may connect a source are listed."
                  error={shown.projectId}
                  data-test="init-project"
                />
                <TextField
                  id="init-remote"
                  label="Git remote"
                  value={draft.remote}
                  onValueChange={(next) => set("remote", next)}
                  placeholder="git@github.com:acme/checkout-web.git"
                  error={shown.remote}
                />
                <FormRow>
                  <TextField
                    id="init-branch"
                    label="Default branch"
                    value={draft.branch}
                    onValueChange={(next) => set("branch", next)}
                    error={shown.branch}
                  />
                  <SwitchField
                    id="init-write"
                    label="May push branches"
                    checked={draft.writeAccess}
                    onCheckedChange={(next) => set("writeAccess", next)}
                    onLabel="on"
                    offLabel="off"
                    hint="Off means the swarm reads the repository and opens nothing."
                  />
                </FormRow>
              </>
            ) : null}

            {step === "compute" ? (
              <FormRow>
                <SelectField
                  id="init-provider"
                  label="Provider"
                  value={draft.provider}
                  onValueChange={(next) => set("provider", next)}
                  options={[
                    { value: "docker", label: "docker" },
                    { value: "kubernetes", label: "kubernetes" },
                    { value: "fly", label: "fly" },
                  ]}
                />
                <TextField
                  id="init-workers"
                  label="Workers at once"
                  value={draft.maxWorkers}
                  onValueChange={(next) => set("maxWorkers", next)}
                  inputMode="numeric"
                  hint="The ceiling on containers this project may hold."
                  error={shown.maxWorkers}
                />
              </FormRow>
            ) : null}

            {step === "models" ? (
              <>
                <TextField
                  id="init-lead"
                  label="Lead model endpoint"
                  value={draft.leadEndpoint}
                  onValueChange={(next) => set("leadEndpoint", next)}
                  placeholder="https://api.example.com/v1"
                  hint="OpenAI- or Anthropic-compatible."
                  error={shown.leadEndpoint}
                />
                <TextField
                  id="init-worker"
                  label="Worker model endpoint"
                  value={draft.workerEndpoint}
                  onValueChange={(next) => set("workerEndpoint", next)}
                  placeholder="leave empty to use the lead endpoint"
                />
                <TextField
                  id="init-secret"
                  label="Secret reference"
                  value={draft.secretRef}
                  onValueChange={(next) => set("secretRef", next)}
                  placeholder="env:ACME_MODEL_KEY"
                  hint="The name of the secret, not the secret. Keys are never typed into this product."
                  error={shown.secretRef}
                />
              </>
            ) : null}

            {step === "knowledge" ? (
              <>
                <SwitchField
                  id="init-knowledge"
                  label="Keep an indexed rule set"
                  checked={draft.knowledge}
                  onCheckedChange={(next) => set("knowledge", next)}
                  onLabel="on"
                  offLabel="off"
                  hint="A docs worker writes it. There is no document editor here."
                />
                <TextareaField
                  id="init-seed"
                  label="Seed"
                  value={draft.seed}
                  onValueChange={(next) => set("seed", next)}
                  rows={3}
                  disabled={!draft.knowledge}
                  hint="What the first pass reads, as globs."
                />
              </>
            ) : null}

            {step === "confirm" ? (
              <>
                <Notice>
                  Nothing has been created yet. Confirming starts the
                  onboarding run, and everything it does is recorded where every
                  other run is.
                </Notice>
                <dl className={styles.review} data-test="init-review">
                  <Review label="project" value={draft.projectId || "—"} />
                  <Review label="remote" value={draft.remote || "—"} />
                  <Review label="branch" value={draft.branch} />
                  <Review
                    label="push access"
                    value={draft.writeAccess ? "on" : "off"}
                  />
                  <Review label="compute" value={draft.provider} />
                  <Review label="workers" value={draft.maxWorkers} />
                  <Review label="lead model" value={draft.leadEndpoint || "—"} />
                  <Review
                    label="worker model"
                    value={draft.workerEndpoint || "same as lead"}
                  />
                  <Review label="secret" value={draft.secretRef || "—"} />
                  <Review
                    label="knowledge"
                    value={draft.knowledge ? draft.seed : "off"}
                  />
                </dl>
              </>
            ) : null}
          </FormFields>

          <FormActions>
            {/* A form's submit keeps its words — it is the act, named. */}
            <Button
              type="submit"
              data-test="init-continue"
              denied={last ? allowed.denial : null}
            >
              {last ? "Start onboarding" : "Continue"}
            </Button>
            <Button type="button" variant="ghost" onClick={back}>
              {index > 0 ? "Back" : "Cancel"}
            </Button>
          </FormActions>
        </FormLayout>
      )}
    </FormPage>
  )
}

function Review({ label, value }: { label: string; value: string }) {
  return (
    <div className={styles.reviewRow}>
      <dt className={styles.reviewLabel}>{label}</dt>
      <dd className={styles.reviewValue}>{value}</dd>
    </div>
  )
}
