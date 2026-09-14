import { PROVIDERS } from "@/domains/sources/model/providers"
import type { ProviderKey } from "@/domains/sources/model/types"
import { providerCardOptions } from "@/domains/sources/ui/provider-card-options"
import { ProviderCards } from "@/shared/ui"

export interface TaskSourceCardsProps {
  value: ProviderKey
  onValueChange: (next: ProviderKey) => void
  disabled?: boolean
  "data-test"?: string
}

/**
 * The first question intake asks: where does this task come from.
 *
 * One card per row of the provider registry, in its order: the trackers a
 * connection can speak, and `native` for the product's own intake. Each card
 * is the entry read out — its mark, its word, and the line that says what
 * picking it means. Adding a provider adds a card and nothing else.
 *
 * The cards themselves are the kit's `ProviderCards` and the reading of a
 * registry row into a card is the sources domain's `providerCardOptions` —
 * this component is the question, not the construction, which is what keeps
 * the intake's row and the connect form's row one device rather than two
 * copies that can drift. The picker carries no heading of its own: its home
 * is a `FormCard` labelled "source" on the create page, and a second label
 * above a card's own label would be the same word twice — the fieldset keeps
 * one, spoken rather than drawn.
 *
 * Yandex Tracker is the one card without a brand mark (no monochrome mark is
 * published; draining the colour glyph leaves an unnameable shape — see
 * `Provider.brand`). It takes a lucide board glyph so the row keeps its
 * rhythm, and its name says the rest.
 */
export function TaskSourceCards({
  value,
  onValueChange,
  disabled = false,
  "data-test": dataTest,
}: TaskSourceCardsProps) {
  return (
    <ProviderCards
      label="source"
      name="task-source"
      value={value}
      disabled={disabled}
      data-test={dataTest}
      cardDataTest="task-source-card"
      labelHidden
      options={providerCardOptions(PROVIDERS)}
      onValueChange={onValueChange}
    />
  )
}
