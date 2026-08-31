import { useState } from "react"
import { useNavigate, useRouter } from "@tanstack/react-router"
import { toast } from "sonner"

import { FormPage } from "@/app/layout/form-page"
import { useUnsavedGuard } from "@/app/layout/use-unsaved-guard"
import {
  useConnectSource,
  useTestSourceDraft,
} from "@/domains/sources/api/mutations"
import type { ProbeResult } from "@/domains/sources/model/types"
import { ConnectSourceForm } from "@/domains/sources/ui/connect-source-form"
import type { SeedSourceDraft } from "@/shared/api/mock/sources.store"
import { can, useSession } from "@/shared/session"
import { ConfirmDialog, Notice } from "@/shared/ui"

/**
 * Connecting a source, on its own screen at its own address.
 *
 * It was a modal over the connections list, and the modal was the wrong shape
 * for it long before there was anywhere better to put it: six fields, a closed
 * credential list, an irreversible rule about a secret and a probe that has to
 * answer before the save opens — folded into `--modal-w`, which is 26rem.
 *
 * The three things a modal never had to answer for, answered here:
 *
 * - **A way back.** The crumb path is `configure / sources / new`, and
 *   `sources` is a real link. Somebody who arrived by typing the URL still has
 *   the list one click away.
 * - **A cancel that returns.** Back through the router's own history when
 *   there is history — the operator lands wherever they pressed *connect a
 *   source*, filters and all — and on `/sources` when there is not, which is
 *   what a bookmarked or pasted URL gets.
 * - **A submit that lands on the thing it made.** The new connection's own
 *   page, which exists now: `/sources/$sourceId`, where the watch it arrived
 *   without is the next decision to take. `replace: true`, because a form that
 *   has already been submitted is not somewhere back should be able to return.
 *
 * The probe lives here rather than in the form for the same reason it lived on
 * the screen rather than in the dialog: any edit has to be able to drop the
 * last answer, and the thing that holds an answer has to be above the thing
 * that invalidates it.
 */
export function ConnectSourcePage() {
  const navigate = useNavigate()
  const router = useRouter()
  const session = useSession()

  const testDraft = useTestSourceDraft()
  const connect = useConnectSource()

  /** The form's last answer, dropped the moment a detail changes. */
  const [probe, setProbe] = useState<ProbeResult | null>(null)
  const [dirty, setDirty] = useState(false)
  const guard = useUnsavedGuard(dirty)

  const cancel = () => {
    guard.leave(() => {
      if (router.history.canGoBack()) {
        router.history.back()
        return
      }
      void navigate({ to: "/sources", search: {} })
    })
  }

  const onCreate = (draft: SeedSourceDraft) => {
    // The button already refuses a denied click, and the handler asks the same
    // question again on the way in: the gate is the permission, not the control
    // that happens to be carrying it today. Asked against the project the form
    // picked, never against the shift.
    if (!can(session, "sources.edit", draft.projectId)) {
      return
    }
    connect.mutate(draft, {
      onSuccess: (created) => {
        toast.success("Source connected", { description: draft.name })
        guard.leave(() => {
          void navigate({
            to: "/sources/$sourceId",
            params: { sourceId: created.id },
            replace: true,
          })
        })
      },
    })
  }

  return (
    <FormPage
      title="Connect a source"
      crumbs={[
        { label: "configure" },
        { label: "sources", to: "/sources" },
        { label: "new" },
      ]}
      summary="A connection belongs to one project and carries one credential. It arrives with its watch off — admitting tickets is a separate decision, taken on the source's own page."
    >
      {connect.error ? (
        <Notice tone="bad" data-test="connect-failure">
          {connect.error.message} Nothing was saved — the details below are
          still exactly as you typed them.
        </Notice>
      ) : null}

      <ConnectSourceForm
        probe={probe}
        probing={testDraft.isPending}
        busy={connect.isPending}
        onTest={(input) => testDraft.mutate(input, { onSuccess: setProbe })}
        /* Any edit drops the last answer: a credential that worked before the
           host was changed is not evidence about the host that is there now. */
        onDraftChange={() =>
          setProbe((current) => (current === null ? current : null))
        }
        onCreate={onCreate}
        onCancel={cancel}
        onDirtyChange={setDirty}
      />

      {/* Leaving a half-filled form is a decision in one sentence, which is
          exactly what a confirm is for — and unlike the form it replaced, it
          asks about something the operator did by accident. The secret is
          named because it is the field nobody can retype from memory. */}
      <ConfirmDialog
        open={guard.asking}
        title="Leave without connecting the source?"
        body="Nothing here is saved yet, and the credential you typed is not held anywhere — leaving this page drops it, and connecting later means fetching it again."
        confirmLabel="Discard"
        cancelLabel="Keep editing"
        onConfirm={guard.discard}
        onCancel={guard.keep}
      />
    </FormPage>
  )
}
