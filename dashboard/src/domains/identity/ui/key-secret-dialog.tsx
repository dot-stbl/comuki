import { Button, FormDialog, Notice, SecretValue } from "@/shared/ui"

/** A key that has just been made: the head that is kept, and the whole secret. */
export interface CreatedKey {
  prefix: string
  plaintext: string
}

export interface KeySecretDialogProps {
  /**
   * The key that was just created, held by the caller for exactly as long as
   * the caller means to show it.
   *
   * A prop rather than state in here, and that is the whole design: the secret
   * has one holder, the holder drops it, and this component has no memory of it
   * to leak on the next render. `null` is closed; a value is the one showing of
   * it there will ever be.
   */
  created: CreatedKey | null
  /** Ends the showing and drops the secret. There is no second way out. */
  onDone: () => void
}

/**
 * The one moment an api key's secret exists on a screen — and the one thing on
 * this section that stayed a modal when everything else became a page.
 *
 * **Why this is not a route.** Editing gets a page here, and a page is a URL:
 * something that can be reloaded, bookmarked, pasted into a ticket, restored
 * by a browser that reopens yesterday's tabs, and re-entered with the back
 * button. Every one of those is a *second* rendering of the same address, and
 * a secret that is shown exactly once cannot live at an address that can be
 * visited twice. The two routed variants both fail on inspection:
 *
 * - A secret in the path or the query is written into history, the referrer
 *   and every proxy log between here and the browser. Not arguable.
 * - A secret carried in navigation state is not much better. TanStack's router
 *   puts location state into `history.state`, which the browser restores on
 *   reload and on a back/forward traversal — so `/identity/keys/created` would
 *   render the plaintext again on F5, which is precisely the thing the product
 *   promises cannot happen. Defending it would mean consuming the state on
 *   mount and replacing the entry, i.e. building a page whose only real
 *   behaviour is refusing to be a page.
 *
 * So the showing stays on the page plane it was created from: React state in
 * `CreateKeyPage`, above the form, gone when the dialog closes. The address bar
 * still reads `/identity/keys/new`; reloading that gives an empty form and
 * nothing else. The form *below* is a page for all the reasons a form should
 * be — room, a breadcrumb, a real cancel — and this is the exception, argued
 * rather than inherited.
 *
 * The footer is a single control on purpose: a cancel here would suggest the
 * key had not been made.
 */
export function KeySecretDialog({ created, onDone }: KeySecretDialogProps) {
  return (
    <FormDialog
      open={created !== null}
      title="Key created"
      submitLabel="Done"
      onSubmit={onDone}
      onCancel={onDone}
      footer={
        <Button data-test="key-done" onClick={onDone}>
          Done
        </Button>
      }
    >
      <Notice>
        This is the only time this secret is shown. Once this dialog closes it
        is gone — the platform keeps the prefix and a hash, and nothing else.
      </Notice>
      {created ? (
        <SecretValue
          id="key-plaintext"
          label="secret"
          value={created.plaintext}
          hint={`Stored as ${created.prefix}. That prefix is all the key list will ever show.`}
        />
      ) : null}
    </FormDialog>
  )
}
