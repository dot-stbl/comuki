import { useState } from "react"
import { ArrowLeft, RotateCw } from "lucide-react"
import { Link, useNavigate, useRouter } from "@tanstack/react-router"
import { toast } from "sonner"

import { FormPage } from "@/app/layout/form-page"
import { useUnsavedGuard } from "@/app/layout/use-unsaved-guard"
import { useCreateNativeTicket } from "@/domains/sources/api/mutations"
import { useSourcesQuery } from "@/domains/sources/api/queries"
import { NativeTicketForm } from "@/domains/sources/ui/native-ticket-form"
import type { SeedTicketDraft } from "@/shared/api/mock/sources.store"
import { requestFailureMessage } from "@/shared/api/problem"
import { can, projectOf, useSession } from "@/shared/session"
import {
  Button,
  ConfirmDialog,
  Notice,
  ScreenState,
  Skeleton,
  Tooltip,
  buttonClass,
} from "@/shared/ui"

/* The register of facts this page waits on, in the rhythm the section's other
   two screens wait in. */
const SKELETON_WIDTHS = ["48%", "76%", "38%", "64%", "52%"]

export interface CreateTicketPageProps {
  /** From the path. The connection the ticket is being filed into. */
  sourceId: string
}

/**
 * Filing a ticket in native intake, at `/sources/<id>/ticket/new`.
 *
 * This one is a *create*, and that is why it did not fold into the source's own
 * page the way the connect and watch forms did. Those two are configuration of
 * a connection: they edit the record the page is about, so they belong on it.
 * A ticket is a different entity with a different lifetime, gated on a
 * different permission and taken by a different person — a member writes one
 * down, a project administrator configures the connection it lands in. Putting
 * it on the detail page would have made one screen answer to two roles.
 *
 * The crumb path names the connection rather than the section, so the way back
 * agrees with the way in: `configure / sources / <connection> / new ticket`,
 * and the third crumb is the page the `+` was pressed on.
 *
 * The address names a source, so it can be stale — a connection that was
 * disconnected while this tab sat open. It says so rather than rendering a form
 * whose submit would land nowhere.
 */
export function CreateTicketPage({ sourceId }: CreateTicketPageProps) {
  const navigate = useNavigate()
  const router = useRouter()
  const session = useSession()
  const { data, isLoading, isError, error, refetch } = useSourcesQuery()
  const createTicket = useCreateNativeTicket()

  const [dirty, setDirty] = useState(false)
  const guard = useUnsavedGuard(dirty)

  const connection =
    data?.connections.find((entry) => entry.id === sourceId) ?? null

  const back = () => {
    if (router.history.canGoBack()) {
      router.history.back()
      return
    }
    void navigate({ to: "/sources/$sourceId", params: { sourceId } })
  }

  const cancel = () => {
    guard.leave(back)
  }

  const onCreate = (draft: SeedTicketDraft) => {
    // The button already refuses a denied click; the handler asks again on the
    // way in, because the gate is the permission rather than the control that
    // happens to be carrying it today. `inbox.take`, on the connection's own
    // project.
    if (!can(session, "inbox.take", draft.projectId)) {
      return
    }
    createTicket.mutate(draft, {
      onSuccess: () => {
        toast.success("Ticket filed", { description: draft.title })
        guard.leave(() => {
          void navigate({
            to: "/sources/$sourceId",
            params: { sourceId },
            replace: true,
          })
        })
      },
    })
  }

  const crumbs = [
    { label: "configure" },
    { label: "sources", to: "/sources" },
    {
      label: connection?.name ?? sourceId,
      to: `/sources/${sourceId}`,
    },
    { label: "new ticket" },
  ]

  /* Three arrivals, not two. The list not having answered yet, the list
     having failed, and the list having answered that no such connection
     exists are different facts about the world, and the page used to fold the
     first two into the third — a dropped request read as "that source is
     gone", in the words of a stale link, with nothing to press. */
  if (isLoading) {
    return (
      <FormPage title="New ticket" crumbs={crumbs}>
        <Skeleton lines={SKELETON_WIDTHS} data-test="ticket-loading" />
      </FormPage>
    )
  }

  if (isError) {
    return (
      <FormPage title="New ticket" crumbs={crumbs}>
        <ScreenState
          kind="error"
          title="Couldn't look this source up"
          description={requestFailureMessage(error, "Unknown error")}
          data-test="ticket-source-failed"
          action={
            <Tooltip content="Retry">
              <Button
                size="icon-sm"
                data-test="ticket-retry"
                aria-label="Retry"
                onClick={() => {
                  void refetch()
                }}
              >
                <RotateCw aria-hidden="true" />
              </Button>
            </Tooltip>
          }
        />
      </FormPage>
    )
  }

  if (!connection) {
    return (
      <FormPage title="New ticket" crumbs={crumbs}>
        <ScreenState
          kind="notFound"
          title="No connection with that id"
          description={`No connection on this platform has the id ${sourceId}. A source that was disconnected while this tab sat open is the ordinary way to arrive here.`}
          data-test="ticket-source-gone"
          action={
            <Tooltip content="Back to sources">
              <Link
                to="/sources"
                search={{}}
                aria-label="Back to sources"
                className={buttonClass({ size: "icon-sm" })}
              >
                <ArrowLeft aria-hidden="true" />
              </Link>
            </Tooltip>
          }
        />
      </FormPage>
    )
  }

  const projectKey =
    projectOf(session, connection.projectId)?.key ?? connection.projectId

  return (
    <FormPage
      title={`New ticket in ${projectKey}`}
      crumbs={crumbs}
      summary="Native intake is the product's own. A ticket written here has no tracker behind it, so its status is the run's status and there is nowhere to sync it back to."
    >
      {createTicket.error ? (
        <Notice tone="bad" data-test="ticket-failure">
          {requestFailureMessage(createTicket.error, "The ticket was refused.")}{" "}
          Nothing was filed — what you typed is still here.
        </Notice>
      ) : null}

      <NativeTicketForm
        projectId={connection.projectId}
        busy={createTicket.isPending}
        onCreate={onCreate}
        onCancel={cancel}
        onDirtyChange={setDirty}
      />

      <ConfirmDialog
        open={guard.asking}
        title="Leave without filing the ticket?"
        body="The title, body and labels you typed are not saved anywhere yet. Leaving this page drops them, and nothing reaches intake."
        confirmLabel="Discard"
        cancelLabel="Keep editing"
        onConfirm={guard.discard}
        onCancel={guard.keep}
      />
    </FormPage>
  )
}
