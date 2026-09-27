import { useState, type ReactNode } from "react"
import type { Meta, StoryObj } from "@storybook/react"
import { Plus, RotateCw } from "lucide-react"

import {
  Button,
  SearchField,
  Section,
  StatusBadge,
  TextField,
  type Status,
} from "@/shared/ui"

/* The kit matrix: every variant of the three controls a screen is most
   often choosing between — buttons, fields, badges — on one page, in the
   words and sizes the product ships. A matrix is not a gallery: each row
   answers a question the operator's hand asks (what does the primary act
   look like next to the quiet one; how loud is a destructive verb; which
   badge step fits a table row), and the answers stand next to each other
   so the choice is a comparison. */

const VARIANTS = ["default", "outline", "secondary", "ghost", "destructive", "link"] as const
const SIZES = ["sm", "default", "lg"] as const
const STATUSES: readonly Status[] = [
  "running",
  "success",
  "failed",
  "waiting",
  "queued",
  "escalated",
  "cancelled",
]

/** The row label — the lab's own voice: data-small mono, the same step the
    tables use for a column's name. */
function RowLabel({ children }: { readonly children: ReactNode }): ReactNode {
  return (
    <span
      style={{
        fontFamily: "var(--font-data)",
        fontSize: "var(--t-micro)",
        letterSpacing: "var(--tracking-data)",
        color: "var(--text-faint)",
        minWidth: "8rem",
      }}
    >
      {children}
    </span>
  )
}

/** One matrix row: a label on the left, the specimens in a wrap on the
    right. Alignment is baseline-honest — `center` vertically, so a row of
    mixed sizes reads as one row rather than three. */
function Row({
  label,
  children,
}: {
  readonly label: string
  readonly children: ReactNode
}): ReactNode {
  return (
    <div
      style={{
        display: "flex",
        alignItems: "center",
        gap: "var(--s5)",
        paddingBlock: "var(--s4)",
        borderTop: "var(--hairline) solid var(--rule)",
      }}
    >
      <RowLabel>{label}</RowLabel>
      <div style={{ display: "flex", alignItems: "center", gap: "var(--s4)", flexWrap: "wrap" }}>
        {children}
      </div>
    </div>
  )
}

/** The search field is controlled; the matrix holds the state so the
    specimen is live rather than a frozen value. */
function LiveSearchField(): ReactNode {
  const [value, setValue] = useState("")
  return (
    <SearchField
      value={value}
      onValueChange={setValue}
      placeholder="search runs…"
      aria-label="Search runs"
    />
  )
}

function Page({ children }: { readonly children: ReactNode }): ReactNode {
  return (
    <div
      style={{
        padding: "var(--page-y) var(--page-x)",
        display: "flex",
        flexDirection: "column",
        gap: "var(--s8)",
        maxWidth: "var(--measure-record)",
        margin: "0 auto",
      }}
    >
      {children}
    </div>
  )
}

const meta: Meta = {
  title: "UI Kit/Style Lab/Kit matrix",
  parameters: { layout: "fullscreen" },
  tags: ["autodocs"],
}

export default meta
type Story = StoryObj

/** Buttons, the whole surface at once: every variant the kit ships, each
    at every text size. One verb per variant keeps the row readable as a
    scale rather than a sentence — and the destructive variant sits at the
    end, where its distance from the primary act is visible rather than
    asserted. */
export const Buttons: Story = {
  render: () => (
    <Page>
      <Section
        variant="screen"
        title="Buttons"
        note="Six variants, three text sizes, the whole decision on one page. The default variant carries the screen's one primary act; outline and secondary carry the rest of the work; ghost is chrome that can act; link is a word that navigates inside a sentence of controls; destructive is a verb that cannot be undone, and it is the only red on the row."
      >
        {VARIANTS.map((variant) => (
          <Row key={variant} label={variant}>
            {SIZES.map((size) => (
              <Button key={size} variant={variant} size={size}>
                Run
              </Button>
            ))}
          </Row>
        ))}
        <Row label="icon sizes">
          <Button size="icon" aria-label="Create">
            <Plus />
          </Button>
          <Button size="icon-sm" aria-label="Retry">
            <RotateCw />
          </Button>
          <Button variant="outline" size="icon" aria-label="Create">
            <Plus />
          </Button>
          <Button variant="ghost" size="icon-sm" aria-label="Retry">
            <RotateCw />
          </Button>
        </Row>
      </Section>
    </Page>
  ),
}

/** The input side of the kit: a labelled text field as the forms lay it
    out, the same field with its label drawn and hidden, and the search
    field the toolbars use — live, so the typing works in the specimen.
    `InputsSpecimen` is a wrapper so `useState` lives at the top of a
    function component, not inside `render` (where
    `react-hooks/rules-of-hooks` would refuse it). */
function InputsSpecimen(): ReactNode {
  const [value, setValue] = useState("worker-04")
  return (
    <Page>
      <Section
        variant="screen"
        title="Inputs"
        note="The labelled field is the unit the forms build from; the label can be hidden when the column or the chrome already names the field, and the search field is the filter bar's own control — wider than a form field by design, because it holds a query rather than a value."
      >
        <div style={{ display: "flex", gap: "var(--s8)", flexWrap: "wrap" }}>
          <div style={{ width: "18rem", display: "flex", flexDirection: "column", gap: "var(--s5)" }}>
            <TextField
              id="kit-matrix-worker"
              label="worker"
              value={value}
              onValueChange={setValue}
            />
            <TextField
              id="kit-matrix-worker-hidden"
              label="worker"
              labelHidden
              placeholder="label hidden"
              value={value}
              onValueChange={setValue}
            />
          </div>
          <div style={{ width: "18rem" }}>
            <LiveSearchField />
          </div>
        </div>
      </Section>
    </Page>
  )
}

export const Inputs: Story = {
  render: () => <InputsSpecimen />,
}

/** Every status at both badge sizes — the closed vocabulary a screen can
    say about a run, in the two steps the kit allows. The `sm` step is the
    table row's; `md` is the panel's. A status not on this page is not a
    status the product can show. */
export const Badges: Story = {
  render: () => (
    <Page>
      <Section
        variant="screen"
        title="Status badges"
        note="Seven statuses, two sizes, one vocabulary. The spelling is the stored value — the badge never title-cases, because the filter beside it does not either. The hue and the hatch both come from the data-status block in tokens.css, so this row is the palette and the weave at once."
      >
        {STATUSES.map((status) => (
          <Row key={status} label={status}>
            <StatusBadge status={status} size="sm" />
            <StatusBadge status={status} size="md" />
          </Row>
        ))}
      </Section>
    </Page>
  ),
}

/** The whole kit matrix on one screen — buttons, inputs, badges — for the
    moment the question is not one control but the loudness of a screen
    that holds several. This is the page to stand back from.
    `EverythingSpecimen` is a wrapper so its `useState` lives at the top
    of a function component, not inside `render` (where
    `react-hooks/rules-of-hooks` would refuse it). */
function EverythingSpecimen(): ReactNode {
  const [value, setValue] = useState("worker-04")
  return (
    <Page>
      <Section
        variant="screen"
        title="Buttons"
        note="Six variants, three text sizes, the whole decision on one page. The default variant carries the screen's one primary act; outline and secondary carry the rest of the work; ghost is chrome that can act; link is a word that navigates inside a sentence of controls; destructive is a verb that cannot be undone, and it is the only red on the row."
      >
        {VARIANTS.map((variant) => (
          <Row key={variant} label={variant}>
            {SIZES.map((size) => (
              <Button key={size} variant={variant} size={size}>
                Run
              </Button>
            ))}
          </Row>
        ))}
        <Row label="icon sizes">
          <Button size="icon" aria-label="Create">
            <Plus />
          </Button>
          <Button size="icon-sm" aria-label="Retry">
            <RotateCw />
          </Button>
          <Button variant="outline" size="icon" aria-label="Create">
            <Plus />
          </Button>
          <Button variant="ghost" size="icon-sm" aria-label="Retry">
            <RotateCw />
          </Button>
        </Row>
      </Section>
      <Section
        variant="screen"
        title="Inputs"
        note="The labelled field is the unit the forms build from; the label can be hidden when the column or the chrome already names the field, and the search field is the filter bar's own control — wider than a form field by design, because it holds a query rather than a value."
      >
        <div style={{ display: "flex", gap: "var(--s8)", flexWrap: "wrap" }}>
          <div style={{ width: "18rem", display: "flex", flexDirection: "column", gap: "var(--s5)" }}>
            <TextField
              id="kit-matrix-worker-everything"
              label="worker"
              value={value}
              onValueChange={setValue}
            />
            <TextField
              id="kit-matrix-worker-hidden-everything"
              label="worker"
              labelHidden
              placeholder="label hidden"
              value={value}
              onValueChange={setValue}
            />
          </div>
          <div style={{ width: "18rem" }}>
            <LiveSearchField />
          </div>
        </div>
      </Section>
      <Section
        variant="screen"
        title="Status badges"
        note="Seven statuses, two sizes, one vocabulary. The spelling is the stored value — the badge never title-cases, because the filter beside it does not either. The hue and the hatch both come from the data-status block in tokens.css, so this row is the palette and the weave at once."
      >
        {STATUSES.map((status) => (
          <Row key={status} label={status}>
            <StatusBadge status={status} size="sm" />
            <StatusBadge status={status} size="md" />
          </Row>
        ))}
      </Section>
    </Page>
  )
}

export const Everything: Story = {
  render: () => <EverythingSpecimen />,
}