import { useEffect, useState } from "react"
import type { FormEvent } from "react"

import { FormActions, FormFields, FormLayout } from "@/app/layout/form-page"
import { ADMISSION_MODES } from "@/domains/sources/model/providers"
import type {
  AdmissionMode,
  SourceConnection,
  SourceWatch,
} from "@/domains/sources/model/types"
import { FilterExpressionField } from "@/domains/sources/ui/filter-expression-field"
import { StatusMappingPreview } from "@/domains/sources/ui/status-mapping-preview"
import { can, needsLabel, projectOf, useSession } from "@/shared/session"
import { Button, ChoiceField, SwitchField } from "@/shared/ui"

export interface WatchFormProps {
  connection: SourceConnection
  /**
   * The watch as the store holds it.
   *
   * Passed beside the connection rather than read off it, because native has
   * none at all — `watch: null` is the honest shape for a source with no remote
   * end, and a form that had to guard its own existence would be the wrong
   * component. The page decides whether there is a watch to edit.
   */
  watch: SourceWatch
  busy?: boolean
  onSave: (patch: {
    enabled: boolean
    filter: string
    mode: AdmissionMode
  }) => void
  /** Tells the page whether there is anything here worth asking about. */
  onDirtyChange?: (dirty: boolean) => void
}

/**
 * Watch and filter: what this connection is allowed to admit, and who moves
 * next when it does.
 *
 * Three things, and they are three because they fail separately. The **switch**
 * decides whether anything is admitted at all. The **filter** decides which
 * tickets — and it is deliberately unparsed; see `FilterExpressionField` for
 * why the language being undecided is load-bearing rather than unfinished. The
 * **admission mode** decides who the ticket lands on, and its three options
 * each carry a sentence, because "watch", "inbox-only" and "both" are three
 * different products for the same ticket and three bare words would be asking
 * the operator to guess which.
 *
 * The status write-back sits underneath as a read-only preview. It belongs to
 * the connector rather than to this form, so there is nothing here that could
 * change it — but turning a watch on is the moment somebody should find out
 * that the swarm is about to start closing their issues.
 *
 * ## Why cancel puts the fields back instead of leaving
 *
 * On a create page, cancel is a way out: there is one form, the page exists
 * only to hold it, and the operator came from somewhere they can be returned
 * to. This form stands on a page that is *about* something — a connection, its
 * facts, its hand-offs — and there are two forms on it. A cancel that navigated
 * would answer a question nobody asked (leave this connection?) instead of the
 * one they did (undo what I typed here). So it reverts to what the store holds,
 * which also drops the page's unsaved guard on its own, because dirty is
 * measured against exactly those values.
 */
export function WatchForm({
  connection,
  watch,
  busy = false,
  onSave,
  onDirtyChange,
}: WatchFormProps) {
  const session = useSession()

  const [enabled, setEnabled] = useState(watch.enabled)
  const [filter, setFilter] = useState(watch.filter)
  const [mode, setMode] = useState<AdmissionMode>(watch.mode)

  const denied = can(session, "sources.edit", connection.projectId)
    ? null
    : needsLabel(
        "sources.edit",
        projectOf(session, connection.projectId)?.key
      )

  /* Measured against the stored watch rather than tracked with a flag: a save
     that lands makes the two agree, so the guard lets go by itself and nothing
     has to remember to clear it. */
  const dirty =
    enabled !== watch.enabled || filter !== watch.filter || mode !== watch.mode

  useEffect(() => {
    onDirtyChange?.(dirty)
  }, [dirty, onDirtyChange])

  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (denied || busy) {
      return
    }
    onSave({ enabled, filter, mode })
  }

  return (
    <FormLayout data-test="watch-form" onSubmit={submit}>
      <FormFields>
        <SwitchField
          id="watch-enabled"
          label="watch this source"
          checked={enabled}
          disabled={busy}
          onLabel="admitting"
          offLabel="admitting nothing"
          denied={denied}
          hint="off means the connection stays authenticated and stops bringing anything in. It is not the same as disconnecting."
          data-test="watch-enabled"
          onCheckedChange={setEnabled}
        />

        {/* Untouched from the dialog it came out of, and that is the point: the
            value is a string from the moment it is typed to the moment the
            store holds it. Nothing on this page trims a clause, normalises
            whitespace or rejects a token. */}
        <FilterExpressionField
          id="watch-filter"
          kind={connection.kind}
          value={filter}
          disabled={busy}
          onValueChange={setFilter}
        />

        <ChoiceField
          name="admission"
          legend="admission mode"
          value={mode}
          disabled={busy}
          options={ADMISSION_MODES}
          data-test="admission-mode"
          onValueChange={(next) => setMode(next as AdmissionMode)}
        />

        <StatusMappingPreview kind={connection.kind} mapping={watch.mapping} />
      </FormFields>

      <FormActions>
        <Button
          type="submit"
          data-test="watch-submit"
          denied={denied}
          disabled={busy}
          aria-busy={busy || undefined}
        >
          Save watch
        </Button>
        <Button
          variant="secondary"
          data-test="watch-cancel"
          disabled={busy}
          onClick={() => {
            setEnabled(watch.enabled)
            setFilter(watch.filter)
            setMode(watch.mode)
          }}
        >
          Cancel
        </Button>
      </FormActions>
    </FormLayout>
  )
}
