import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type RefObject,
} from "react"
import { useNavigate, useRouter } from "@tanstack/react-router"
import { useTranslation } from "react-i18next"

import {
  FormActions,
  FormFields,
  FormLayout,
  FormPage,
  FormRow,
} from "@/app/layout/form-page"
import { useUnsavedGuard } from "@/app/layout/use-unsaved-guard"
import {
  EMPTY_DRAFT,
  INIT_STAGES,
  INIT_STEPS,
  initStageLabel,
  initProjects,
  stepErrors,
  stepIndex,
  stepSummary,
  stepTitle,
  type InitDraft,
  type InitStep,
} from "@/domains/chat/model/init-wizard"
import { cn } from "@/shared/lib/utils"
import { useCan, useSession } from "@/shared/session"
import {
  Button,
  ConfirmDialog,
  Notice,
  NumberField,
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
  const { t } = useTranslation("chat")
  const { t: tShell } = useTranslation("shell")

  const projects = useMemo(() => initProjects(session), [session])
  /* What the wizard was handed when it opened — the empty draft, plus the
     project the console scoped it to. Held so "unsaved" can be measured
     against it rather than against `EMPTY_DRAFT`: arriving from `/chat` with a
     project already chosen is not something the operator typed. */
  const [opened] = useState<InitDraft>(() => ({
    ...EMPTY_DRAFT,
    projectId:
      project && projects.some((entry) => entry.id === project) ? project : "",
  }))
  const [draft, setDraft] = useState<InitDraft>(opened)
  const [showErrors, setShowErrors] = useState(false)
  const [running, setRunning] = useState(false)
  const [stage, setStage] = useState(0)

  /* Coarse the way `useUnsavedGuard` asks for it: anything off what the wizard
     opened with counts, across every step rather than the one showing — a
     remote typed on step one is still unsaved while the operator is looking at
     step three. Once the stream is running nothing is unsaved any more: the
     act has started and there is nothing left to drop. */
  const dirty =
    !running &&
    (Object.keys(opened) as (keyof InitDraft)[]).some(
      (key) => draft[key] !== opened[key]
    )

  /* The current step's `leave`, handed up by the guard below. Wrapping a
     departure the operator actually asked for is what keeps the guard from
     asking "are you sure" about the button they just pressed. */
  const leaveRef = useRef<(go: () => void) => void>((go) => {
    go()
  })
  const leave = useCallback((go: () => void) => {
    leaveRef.current(go)
  }, [])

  const errors = stepErrors(step, draft, t)
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
      // A step lives in the address, so moving between steps *is* a
      // navigation as far as the router's blocker is concerned — and pressing
      // Continue is the last thing that should be met with "leave without
      // saving?". Every step change is therefore a departure the operator
      // asked for, and `StepGuard` re-arms itself on the other side of it.
      leave(() => {
        void navigate({
          to: "/chat/init",
          search: { step: next },
          replace: true,
        })
      })
    },
    [navigate, leave]
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
    // Cancel, on the first step. The operator said to leave, so they are not
    // asked about it — the guard exists for the rail, the crumb and the URL
    // bar, not for the button whose whole word is "cancel".
    leave(() => {
      if (router.history.canGoBack()) {
        router.history.back()
        return
      }
      void navigate({ to: "/chat" })
    })
  }

  return (
    <FormPage
      title={t("wizard.title")}
      crumbs={[
        { label: tShell("crumb.console"), to: "/chat" },
        { label: tShell("crumb.onboard") },
      ]}
      summary={running ? t("wizard.running") : stepSummary(step, t)}
    >
      {/* Keyed by the step, and that is the whole trick — see `StepGuard`. */}
      <StepGuard key={step} dirty={dirty} leaveRef={leaveRef} />

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
            <span className={styles.stepName}>{stepTitle(entry, t)}</span>
          </li>
        ))}
      </ol>

      {running ? (
        <section className={styles.stream} data-test="init-stream">
          <h2 className={styles.streamHead}>{t("wizard.streamHead")}</h2>
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
                  {at < stage
                    ? t("wizard.stageWord.done")
                    : at === stage
                      ? t("wizard.stageWord.running")
                      : t("wizard.stageWord.queued")}
                </StatusBadge>
                <span className={styles.stageLabel}>
                  {initStageLabel(at, t)}
                </span>
              </li>
            ))}
          </ol>
          {stage >= INIT_STAGES.length ? (
            <>
              <Notice tone="ok" announce data-test="init-done">
                {t("wizard.doneNotice")}
              </Notice>
              <FormActions>
                <Button
                  onClick={() => {
                    leave(() => {
                      void navigate({ to: "/chat" })
                    })
                  }}
                >
                  {t("wizard.backToConsole")}
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
                  label={t("wizard.field.project")}
                  required
                  value={draft.projectId}
                  onValueChange={(next) => set("projectId", next)}
                  options={projects.map((entry) => ({
                    value: entry.id,
                    label: entry.key,
                  }))}
                  placeholder={t("wizard.field.pickProject")}
                  hint={t("wizard.field.projectHint")}
                  error={shown.projectId}
                  data-test="init-project"
                />
                <TextField
                  id="init-remote"
                  label={t("wizard.field.remote")}
                  required
                  value={draft.remote}
                  onValueChange={(next) => set("remote", next)}
                  placeholder="git@github.com:acme/checkout-web.git"
                  error={shown.remote}
                />
                <FormRow>
                  <TextField
                    id="init-branch"
                    label={t("wizard.field.branch")}
                    required
                    value={draft.branch}
                    onValueChange={(next) => set("branch", next)}
                    error={shown.branch}
                  />
                  <SwitchField
                    id="init-write"
                    label={t("wizard.field.write")}
                    checked={draft.writeAccess}
                    onCheckedChange={(next) => set("writeAccess", next)}
                    hint={t("wizard.field.writeHint")}
                  />
                </FormRow>
              </>
            ) : null}

            {step === "compute" ? (
              <FormRow>
                <SelectField
                  id="init-provider"
                  label={t("wizard.field.provider")}
                  value={draft.provider}
                  onValueChange={(next) => set("provider", next)}
                  options={[
                    { value: "docker", label: "docker" },
                    { value: "kubernetes", label: "kubernetes" },
                    { value: "fly", label: "fly" },
                  ]}
                />
                <NumberField
                  id="init-workers"
                  label={t("wizard.field.workers")}
                  required
                  unit="workers"
                  min={1}
                  value={draft.maxWorkers}
                  onValueChange={(next) => set("maxWorkers", next)}
                  hint={t("wizard.field.workersHint")}
                  error={shown.maxWorkers}
                />
              </FormRow>
            ) : null}

            {step === "models" ? (
              <>
                <TextField
                  id="init-lead"
                  label={t("wizard.field.lead")}
                  required
                  value={draft.leadEndpoint}
                  onValueChange={(next) => set("leadEndpoint", next)}
                  placeholder="https://api.example.com/v1"
                  hint={t("wizard.field.leadHint")}
                  error={shown.leadEndpoint}
                />
                <TextField
                  id="init-worker"
                  label={t("wizard.field.workerModel")}
                  value={draft.workerEndpoint}
                  onValueChange={(next) => set("workerEndpoint", next)}
                  placeholder={t("wizard.field.workerPlaceholder")}
                />
                <TextField
                  id="init-secret"
                  label={t("wizard.field.secret")}
                  required
                  value={draft.secretRef}
                  onValueChange={(next) => set("secretRef", next)}
                  placeholder="env:ACME_MODEL_KEY"
                  hint={t("wizard.field.secretHint")}
                  error={shown.secretRef}
                />
              </>
            ) : null}

            {step === "knowledge" ? (
              <>
                <SwitchField
                  id="init-knowledge"
                  label={t("wizard.field.knowledge")}
                  checked={draft.knowledge}
                  onCheckedChange={(next) => set("knowledge", next)}
                  hint={t("wizard.field.knowledgeHint")}
                />
                <TextareaField
                  id="init-seed"
                  label={t("wizard.field.seed")}
                  value={draft.seed}
                  onValueChange={(next) => set("seed", next)}
                  rows={3}
                  disabled={!draft.knowledge}
                  hint={t("wizard.field.seedHint")}
                />
              </>
            ) : null}

            {step === "confirm" ? (
              <>
                <Notice>{t("wizard.confirmNotice")}</Notice>
                <dl className={styles.review} data-test="init-review">
                  <Review
                    label={t("wizard.review.project")}
                    value={draft.projectId || "—"}
                  />
                  <Review
                    label={t("wizard.review.remote")}
                    value={draft.remote || "—"}
                  />
                  <Review
                    label={t("wizard.review.branch")}
                    value={draft.branch}
                  />
                  <Review
                    label={t("wizard.review.pushAccess")}
                    value={
                      draft.writeAccess
                        ? t("wizard.review.on")
                        : t("wizard.review.off")
                    }
                  />
                  <Review
                    label={t("wizard.review.compute")}
                    value={draft.provider}
                  />
                  <Review
                    label={t("wizard.review.workers")}
                    value={draft.maxWorkers}
                  />
                  <Review
                    label={t("wizard.review.lead")}
                    value={draft.leadEndpoint || "—"}
                  />
                  <Review
                    label={t("wizard.review.workerModel")}
                    value={
                      draft.workerEndpoint || t("wizard.review.sameAsLead")
                    }
                  />
                  <Review
                    label={t("wizard.review.secret")}
                    value={draft.secretRef || "—"}
                  />
                  <Review
                    label={t("wizard.review.knowledge")}
                    value={
                      draft.knowledge ? draft.seed : t("wizard.review.off")
                    }
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
              {last ? t("wizard.start") : t("wizard.continue")}
            </Button>
            <Button type="button" variant="ghost" onClick={back}>
              {index > 0 ? t("wizard.back") : t("wizard.cancel")}
            </Button>
          </FormActions>
        </FormLayout>
      )}
    </FormPage>
  )
}

/**
 * The unsaved guard, re-armed at every step.
 *
 * The wizard is the one create-page in the product whose *own* controls
 * navigate: the step is a search parameter, so pressing Continue is a router
 * navigation and the blocker sees it exactly as it sees somebody clicking the
 * rail. The way a page tells the guard "this departure was the point" is
 * `guard.leave`, and `leave` sets a ref that is never cleared — one Continue
 * and the guard is disarmed for the life of the component, which on this page
 * is the life of the whole wizard. Half the flow would then be unguarded, and
 * the half that collects the git remote and the secret reference is the half
 * that would lose it.
 *
 * So the guard is not mounted by the page; it is mounted by this component,
 * keyed on the step. A step change is a remount, a remount is a fresh ref, and
 * the guard arrives at step two armed. The alternative — teaching
 * `useUnsavedGuard` to read `{ current, next }` off the blocker and let a
 * same-route step change through — is the better fix and belongs in
 * `app/layout/use-unsaved-guard.ts`, which is not this page's file.
 *
 * `leaveRef` rather than a render prop, so the form stays the form: the page
 * calls `leaveRef.current(go)` and does not have to be rebuilt around a
 * callback that only exists to reach the blocker.
 */
function StepGuard({
  dirty,
  leaveRef,
}: {
  dirty: boolean
  leaveRef: RefObject<(go: () => void) => void>
}) {
  const guard = useUnsavedGuard(dirty)
  const { t } = useTranslation("chat")

  useEffect(() => {
    leaveRef.current = guard.leave
  }, [guard.leave, leaveRef])

  return (
    <ConfirmDialog
      open={guard.asking}
      title={t("wizard.guardTitle")}
      body={t("wizard.guardBody")}
      confirmLabel={t("wizard.guardConfirm")}
      cancelLabel={t("wizard.guardCancel")}
      onConfirm={guard.discard}
      onCancel={guard.keep}
    />
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
