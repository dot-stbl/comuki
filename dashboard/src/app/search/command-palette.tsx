import { useMemo } from "react"
import { Search } from "lucide-react"
import {
  Autocomplete,
  Dialog,
  Header,
  Input,
  Menu,
  MenuItem,
  MenuSection,
  Modal,
  ModalOverlay,
  TextField,
} from "react-aria-components"

import { cn } from "@/shared/lib/utils"

import { GROUP_LABELS, GROUP_ORDER, type SearchItem } from "./resolve"

import styles from "./command-palette.module.css"

export interface CommandPaletteProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  /** The typed query. Held by the caller so a hand-off can read it. */
  query: string
  onQueryChange: (next: string) => void
  /** Rows to show, already resolved and already access-filtered. */
  items: SearchItem[]
  /** What enter and a click do. The palette never navigates itself. */
  onSelect: (item: SearchItem) => void
  /** What to say when nothing answers. */
  emptyLabel?: string
}

/**
 * The palette: a search field over a keyboard-driven list of destinations.
 *
 * **Fully controlled, and deliberately ignorant.** It takes rows and hands back
 * the one that was chosen; it does not resolve, does not know a run id from a
 * section, and does not navigate. Everything it would need to know to do any of
 * those is product policy, and that lives one file over in `global-search.tsx`.
 * The payoff is that every state this thing can be in — resting, resolving,
 * ambiguous, handing off, empty — is a prop away in a story and in a test.
 *
 * React Aria owns the behaviour that is genuinely hard: the focus trap and the
 * escape key come from `Modal`, and `Autocomplete` is what lets the arrow keys
 * drive a list while the caret stays in the field — the input keeps real focus
 * and the list is moved through `aria-activedescendant`, which is the only
 * spelling of this pattern a screen reader reads correctly.
 */
export function CommandPalette({
  open,
  onOpenChange,
  query,
  onQueryChange,
  items,
  onSelect,
  emptyLabel = "nothing here answers to that",
}: CommandPaletteProps) {
  const byId = useMemo(
    () => new Map(items.map((item) => [item.id, item])),
    [items]
  )

  // The bands, in the order the resolver's three layers produced them. A band
  // with nothing in it is not rendered — a heading standing over nothing is a
  // more confusing artefact than the missing rows were.
  const bands = useMemo(
    () =>
      GROUP_ORDER.map((group) => ({
        group,
        label: GROUP_LABELS[group],
        rows: items.filter((item) => item.group === group),
      })).filter((band) => band.rows.length > 0),
    [items]
  )

  return (
    <ModalOverlay
      isOpen={open}
      onOpenChange={onOpenChange}
      isDismissable
      className={styles.scrim}
    >
      <Modal className={styles.modal}>
        <Dialog
          className={styles.dialog}
          aria-label="Search Comuki"
          data-test="command-palette"
        >
          {/* No `filter`: the rows arriving as props are already the answer,
              and letting the autocomplete narrow them a second time would put
              a substring match on top of a resolver that deliberately is not
              one. It is here for the keyboard, not for the filtering. */}
          <Autocomplete inputValue={query} onInputChange={onQueryChange}>
            <div className={styles.field}>
              <Search className={styles.fieldIcon} aria-hidden="true" />
              {/* A text field rather than a search field, and the difference
                  is one key: React Aria's search field swallows escape to
                  clear itself, which would cost the palette the gesture that
                  closes it. Clearing is what backspace is for. */}
              <TextField
                aria-label="Search Comuki"
                className={styles.textField}
                autoFocus
              >
                <Input
                  className={styles.input}
                  data-test="command-palette-input"
                  placeholder="paste an id, or name a screen…"
                />
              </TextField>
            </div>

            {bands.length > 0 ? (
              <Menu
                className={styles.list}
                aria-label="Results"
                data-test="command-palette-list"
                onAction={(key) => {
                  const item = byId.get(String(key))
                  if (item) {
                    onSelect(item)
                  }
                }}
              >
                {bands.map((band) => (
                  <MenuSection
                    key={band.group}
                    className={styles.band}
                    data-test="command-palette-band"
                  >
                    <Header className={styles.bandLabel}>{band.label}</Header>
                    {band.rows.map((item) => (
                      <MenuItem
                        key={item.id}
                        id={item.id}
                        textValue={`${item.kind} ${item.label}`}
                        className={styles.item}
                        data-test="command-palette-item"
                        data-kind={item.kind}
                        data-href={item.href}
                      >
                        <span className={styles.itemKind}>{item.kind}</span>
                        <span
                          className={cn(
                            styles.itemLabel,
                            item.value && styles.itemValue
                          )}
                        >
                          {item.label}
                        </span>
                        {item.hint ? (
                          <span className={styles.itemHint}>{item.hint}</span>
                        ) : null}
                      </MenuItem>
                    ))}
                  </MenuSection>
                ))}
              </Menu>
            ) : (
              <p className={styles.empty} data-test="command-palette-empty">
                {emptyLabel}
              </p>
            )}
          </Autocomplete>

          {/* The three keys, said once, where they are needed. Everything the
              palette does is a keyboard gesture, and a control whose whole
              interface is invisible has to name it somewhere. */}
          <footer className={styles.hints} aria-hidden="true">
            <span>↑↓ move</span>
            <span>enter open</span>
            <span>esc close</span>
          </footer>
        </Dialog>
      </Modal>
    </ModalOverlay>
  )
}
