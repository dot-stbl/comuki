import { useState } from "react"
import { ArrowLeft } from "lucide-react"
import { Link, useNavigate, useRouter } from "@tanstack/react-router"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { FormPage } from "@/app/layout/form-page"
import { useUnsavedGuard } from "@/app/layout/use-unsaved-guard"
import {
  useIdentityQuery,
  useLinkOidcMutation,
} from "@/domains/identity/api/queries"
import type { LinkOidcInput } from "@/domains/identity/model/types"
import { LinkOidcForm } from "@/domains/identity/ui/link-oidc-form"
import { requestFailureMessage } from "@/shared/api/problem"
import { ConfirmDialog, Notice, Tooltip, buttonClass } from "@/shared/ui"

export interface LinkOidcPageProps {
  /** From the path. An account is a thing, so editing one has an address. */
  userId: string
}

/**
 * Linking a provider subject to an account, at `/identity/users/<id>/link`.
 *
 * This is the one flow in the section that edits something that already
 * exists, which is why it is the one whose URL names a subject. It is also why
 * it has two states a create form never has: the account may not exist, and it
 * may already be linked. Both are reachable by typing the URL or by coming
 * back to a stale tab, and both say so rather than rendering a form that would
 * fail on submit.
 */
export function LinkOidcPage({ userId }: LinkOidcPageProps) {
  const navigate = useNavigate()
  const router = useRouter()
  const { t } = useTranslation("identity")
  const { t: tShell } = useTranslation("shell")
  const { data, isLoading } = useIdentityQuery()
  const linkOidc = useLinkOidcMutation()

  const [dirty, setDirty] = useState(false)
  const guard = useUnsavedGuard(dirty)

  const user = data?.users.find((entry) => entry.id === userId) ?? null

  /* The person is this page's parent now that they have a page of their own,
     so the path back runs through them: platform / identity / <the person> /
     link an oidc subject.

     Where the account is not known yet — the payload has not landed, or the id
     resolves to nothing — the crumb is left out rather than rendered against
     an id. A crumb that names a thing nobody recognises, or that points at a
     page which is about to say "no account with that id", is worse than a
     shorter path. */
  const crumbs = user
    ? [
        { label: tShell("crumb.platform") },
        { label: tShell("crumb.identity"), to: "/identity" },
        { label: user.email, to: `/identity/users/${user.id}` },
        { label: t("linkPage.crumb") },
      ]
    : [
        { label: tShell("crumb.platform") },
        { label: tShell("crumb.identity"), to: "/identity" },
        { label: t("linkPage.crumb") },
      ]

  /* Back to the person, not back to the list.
     The operator arrived from an account and belongs on it afterwards — the
     list they would otherwise land on is one they have already left. History
     still wins where there is any, because the screen behind them may have
     been narrowed and a fresh navigation would drop the filter; only the
     fallback, for a pasted link or a fresh tab, changed. */
  const cancel = () => {
    guard.leave(() => {
      if (router.history.canGoBack()) {
        router.history.back()
        return
      }
      void navigate({ to: "/identity/users/$userId", params: { userId } })
    })
  }

  const onLink = (input: LinkOidcInput) => {
    linkOidc.mutate(input, {
      onSuccess: () => {
        toast.message(t("linkPage.toast"), { description: input.subject })
        guard.leave(() => {
          // The subject was written *on this person*, so the answer is shown
          // on this person — with the new subject already in the facts, which
          // is the confirmation that a filtered list could only imply.
          // `replace`, because a form that has been submitted is not a place
          // to go back to.
          void navigate({
            to: "/identity/users/$userId",
            params: { userId },
            replace: true,
          })
        })
      },
    })
  }

  if (!user) {
    return (
      <FormPage title={t("linkPage.title")} crumbs={crumbs}>
        <Notice tone={isLoading ? "warn" : "bad"} data-test="user-missing">
          {isLoading ? t("linkPage.lookingUp") : t("linkPage.missing")}
        </Notice>
        {/* Nowhere to send them but the section: there is no person here to
            go back to, which is the whole content of this state. */}
        <span>
          <Tooltip content={t("linkPage.backToIdentity")}>
            <Link
              to="/identity"
              aria-label={t("linkPage.backToIdentity")}
              className={buttonClass({ size: "icon-sm" })}
            >
              <ArrowLeft aria-hidden="true" />
            </Link>
          </Tooltip>
        </span>
      </FormPage>
    )
  }

  if (user.oidcSubject) {
    return (
      <FormPage
        title={t("linkPage.title")}
        crumbs={crumbs}
        summary={t("linkPage.summaryLinked", { email: user.email })}
      >
        {/* Relinking is not an act this product has. Offering a form that
            would overwrite a subject silently would be inventing one. */}
        <Notice tone="ok" data-test="already-linked">
          {t("linkPage.alreadyLinked", { subject: user.oidcSubject })}
        </Notice>
        {/* There *is* a person here, so the way out is them rather than the
            section. The name says which account, because "back" on its own is
            not the name of anywhere. */}
        <span>
          <Tooltip content={t("linkPage.backToUser", { email: user.email })}>
            <Link
              to="/identity/users/$userId"
              params={{ userId }}
              aria-label={t("linkPage.backToUser", { email: user.email })}
              className={buttonClass({ size: "icon-sm" })}
            >
              <ArrowLeft aria-hidden="true" />
            </Link>
          </Tooltip>
        </span>
      </FormPage>
    )
  }

  return (
    <FormPage
      title={t("linkPage.title")}
      crumbs={crumbs}
      summary={t("linkPage.summary", { email: user.email })}
    >
      {/* The page already had two Notices for the states it can arrive in;
          this is the one for the state it can be left in. The host's own
          sentence, because "a subject is written once" is exactly the kind of
          rule the platform answers with and the transport does not. */}
      {linkOidc.error ? (
        <Notice tone="bad" data-test="link-failure">
          {requestFailureMessage(linkOidc.error, t("linkPage.refused"))}{" "}
          {t("linkPage.tail")}
        </Notice>
      ) : null}

      <LinkOidcForm
        user={user}
        busy={linkOidc.isPending}
        onLink={onLink}
        onCancel={cancel}
        onDirtyChange={setDirty}
      />

      <ConfirmDialog
        open={guard.asking}
        title={t("linkPage.leaveTitle")}
        body={t("linkPage.leaveBody")}
        confirmLabel={t("linkPage.discard")}
        cancelLabel={t("linkPage.keep")}
        onConfirm={guard.discard}
        onCancel={guard.keep}
      />
    </FormPage>
  )
}
