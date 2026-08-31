import { Children, type FormEvent, type ReactNode } from "react"

import { AppShell } from "@/app/layout/app-shell"
import { PageHeader, type PageHeaderCrumb } from "@/app/layout/page-header"

import styles from "./form-page.module.css"

export interface FormPageProps {
  /** The act, named once. The page's only `h1`. */
  title: string
  /** The path back, this page last. The one before it is where cancel goes. */
  crumbs: PageHeaderCrumb[]
  /** What this form does, in a sentence or two, under the title. */
  summary?: ReactNode
  /**
   * The screen's own controls, at the end of the crumb line — `PageHeader`'s
   * `actions` slot, handed straight through.
   *
   * A create form has none: the only two verbs it has are its own submit and
   * cancel, and both belong in `FormActions` at the end of the column. An
   * *edit* page for a thing that already exists is where this earns its keep —
   * a source connection's state badge, its test-connection probe, its
   * disconnect. Those act on the record rather than on the draft, so they must
   * not sit in the footer beside "save": a footer is what this form does, and
   * disconnecting is not a way of saving it.
   */
  actions?: ReactNode
  children: ReactNode
}

/**
 * The frame a form gets now that it is a screen rather than a modal.
 *
 * A dialog answered none of the questions a page has to: there was no path
 * back except the button in its own footer, no address to send somebody, and
 * no room — `--modal-w` is 26rem, and every field in this product was being
 * folded into it. This is the same three parts every other screen has (shell,
 * header, content) with the content constrained to a readable measure instead
 * of a modal's width.
 *
 * `padded` is left at its default, which is the whole trick with the height
 * chain: `AppShell` gives the block padding and owns the scroll, and this
 * column declares no height at all, so it ends where the last field ends.
 *
 * ## Why this lives with the shell rather than in the kit
 *
 * It was written twice, once in `domains/identity` and once in
 * `domains/projects`, each with a note naming the other and both agreeing that
 * a third section wanting a form page was the moment to promote it. Tasks and
 * sources are that third and fourth section.
 *
 * The promotion had two plausible homes and the precedent decided it:
 * `require-permission.tsx` sits here rather than in `shared/ui` because it
 * knows the product's permissions and the product's shell, and a kit part is
 * not allowed to know either. This knows the same two things one layer along —
 * it renders `AppShell`, it fills `PageHeader`, and its crumbs are router
 * destinations. A `shared/ui` component that imported the shell would invert
 * the dependency the kit exists to keep; a component that took the shell as a
 * prop would be a worse `AppShell`. So it is shell chrome that screens
 * parameterise, exactly like `PageHeader` beside it.
 *
 * What it deliberately does *not* own is the `<form>` element. The form itself
 * stays a component in its own domain, with no router, no shell and no
 * mutation in it, so the fields and the rules about them can be tested on
 * their own — which is the split every screen here already uses and the reason
 * `create-project-form.test.tsx` needs no router at all. The furniture that
 * shape needs is exported beside this, from one stylesheet, because the page
 * and the form on it are one gesture.
 */
export function FormPage({
  title,
  crumbs,
  summary,
  actions,
  children,
}: FormPageProps) {
  return (
    <AppShell
      header={
        <PageHeader
          breadcrumbs={crumbs}
          title={title}
          summary={
            summary ? (
              <span className={styles.summary}>{summary}</span>
            ) : undefined
          }
          actions={actions}
        />
      }
    >
      <div className={styles.column} data-test="form-page">
        {children}
      </div>
    </AppShell>
  )
}

export interface FormLayoutProps {
  onSubmit: (event: FormEvent) => void
  children: ReactNode
  "data-test"?: string
}

/**
 * The `<form>` on a form page, and the rhythm between its regions.
 *
 * `noValidate` is not optional and is therefore not a prop: the browser's own
 * validation bubble is a second voice saying a different sentence about the
 * same field, in wording this product did not choose and cannot translate.
 * Every rule in this product is written under the control it belongs to, in
 * the product's own words, and a form that let the platform speak too would
 * have two of them.
 */
export function FormLayout({
  onSubmit,
  children,
  "data-test": dataTest,
}: FormLayoutProps) {
  return (
    <form
      className={styles.form}
      data-test={dataTest}
      noValidate
      onSubmit={onSubmit}
    >
      {children}
    </form>
  )
}

/**
 * The questions, one under the other.
 *
 * A step wider than a dialog's gap between fields: on a page the fields are
 * the only thing in the column, so the gap is what separates one question from
 * the next — in a dialog the modal's edge was doing half that work.
 */
export function FormFields({ children }: { children: ReactNode }) {
  return <div className={styles.fields}>{children}</div>
}

/**
 * Two fields that are one decision — a lifetime and its unit, a scope and its
 * target — on one line, wrapping rather than crushing.
 *
 * Each child is given its own track here rather than at the call site: a row
 * whose items are wrapped by hand is a row where one of them will eventually
 * be wrapped differently. A conditional child that renders nothing takes no
 * track at all, so a row that is sometimes one field wide stays one field
 * wide.
 */
export function FormRow({ children }: { children: ReactNode }) {
  return (
    <div className={styles.row}>
      {Children.map(children, (child) =>
        child === null || child === undefined || child === false ? null : (
          <div className={styles.rowItem}>{child}</div>
        )
      )}
    </div>
  )
}

/**
 * What the form does, at the end of it.
 *
 * Bounded by a hairline above it, like every other region in this product —
 * not floated, not sticky. The primary act leads because the column is read
 * left to right and this one is left-aligned; a dialog puts it last because a
 * dialog's footer is right-aligned.
 */
export function FormActions({ children }: { children: ReactNode }) {
  return <div className={styles.footer}>{children}</div>
}
