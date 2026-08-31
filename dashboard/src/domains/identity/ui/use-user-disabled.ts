import { useState } from "react"
import { toast } from "sonner"

import { useSetUserDisabledMutation } from "@/domains/identity/api/queries"
import type { UserRow } from "@/domains/identity/model/types"
import type { ConfirmDialogProps } from "@/shared/ui"

/**
 * Switching an account off, and back on — the one act, spelled once.
 *
 * Two screens now perform it: the row in the people list, and the person's own
 * page. What they share is not the button — one is an icon in a table cell and
 * the other rides the header of a screen about a single account — but the
 * *sentence*, and the asymmetry underneath it: disabling somebody is a question
 * and enabling them is not. Two copies of that would have been two copies of a
 * promise ("their grants stay as they are"), and a promise that drifts between
 * two screens is worse than one that was never made.
 *
 * It is a hook returning a props object rather than a component wrapping a
 * dialog, for two reasons. The trigger cannot be inside it — a `cell` is not a
 * component, so the list's button is built by a plain function far from any
 * dialog — and a file that exported both a hook and a component would lose fast
 * refresh, which is the same reason `model/tabs.ts` is its own file. So the
 * call site writes `<ConfirmDialog {...act.dialog} />` and owns nothing but the
 * placement.
 */
export interface UserDisabledAct {
  /** The account a write is currently running against, if any. */
  busyId: string | null
  /**
   * The act. Enabling runs immediately — turning an account back on is not
   * destructive and asks nothing. Disabling opens the question below.
   */
  toggle: (user: UserRow) => void
  /** Spread onto a `ConfirmDialog`. The sentence lives here and nowhere else. */
  dialog: ConfirmDialogProps
}

export function useUserDisabledAct(): UserDisabledAct {
  const setDisabled = useSetUserDisabledMutation()
  const [asking, setAsking] = useState<UserRow | null>(null)

  const busyId = setDisabled.isPending
    ? (setDisabled.variables?.userId ?? null)
    : null

  const toggle = (user: UserRow) => {
    if (user.status === "disabled") {
      setDisabled.mutate({ userId: user.id, disabled: false })
      return
    }
    setAsking(user)
  }

  const confirm = () => {
    const user = asking
    // Closed before the write, so a second press cannot queue a second one.
    setAsking(null)
    if (!user) {
      return
    }
    setDisabled.mutate(
      { userId: user.id, disabled: true },
      {
        onSuccess: () => {
          toast.message("Account disabled", { description: user.email })
        },
      }
    )
  }

  return {
    busyId,
    toggle,
    dialog: {
      open: asking !== null,
      danger: true,
      /* Switching an account off keeps its grants: disabling somebody and
         un-granting them are different acts, and the confirmation says which
         one is about to happen. */
      title: "Disable this account?",
      body: asking
        ? `${asking.email} will not be able to sign in. Their grants stay as they are, and enabling the account restores them exactly.`
        : "",
      confirmLabel: "Disable",
      cancelLabel: "Cancel",
      onCancel: () => setAsking(null),
      onConfirm: confirm,
    },
  }
}
