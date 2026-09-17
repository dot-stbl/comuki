import type { ButtonHTMLAttributes, MouseEvent, Ref } from "react"
import { Loader2 } from "lucide-react"

import { cn } from "@/shared/lib/utils"

import { useInTooltip } from "./tooltip-context"

import styles from "./button.module.css"

type Variant =
  "default" | "outline" | "secondary" | "ghost" | "destructive" | "link"

type Size = "default" | "sm" | "lg" | "icon" | "icon-sm" | "icon-lg"

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant
  size?: Size
  /**
   * The act exists but this role may not perform it — pass the sentence that
   * says what is missing (`"needs approver"`). The button stays where it was,
   * looks unavailable, and refuses the click.
   *
   * Deliberately not `disabled`. A disabled control fires no pointer events, so
   * its `title` never appears and it drops out of the tab order — the operator
   * gets a dead grey shape and no reason. `aria-disabled` keeps it focusable
   * and hoverable, which is the only way the explanation is actually reachable
   * by either a pointer or a keyboard. Three states, three props: `loading` for
   * *busy*, `disabled` for *invalid*, `denied` for *not yours*. Busy shared
   * `disabled` with invalid until it had earned its own prop: every busy button
   * in the product had hand-rolled the same `aria-busy` beside it, and thirteen
   * of them the same spinner, in nine stylesheets.
   */
  denied?: string | null
  /**
   * The act is running — the click landed and the answer has not. One prop
   * says it three ways at once, because the product kept saying it in three
   * dialects: `aria-busy` for anything listening, a native `disabled` so a
   * second click cannot start a second act, and the kit's spinner where the
   * operator is already looking. Nothing here was invented — it is the three
   * spellings the product already had, agreeing for the first time.
   *
   * Where the spinner goes is read off `size`, which is what keeps this to one
   * prop instead of two. At the icon sizes the whole content is a single glyph,
   * so the spinner takes its place — there is nothing else in the box to keep,
   * and the name was never the glyph anyway. At the text sizes it leads the
   * label and the label stays: swapping the word for a participle ("Save" →
   * "Saving…") resizes the button under the pointer and spends the operator's
   * one reading of *what* is running.
   *
   * `disabled` stays the call site's for the *invalid* half — an empty required
   * field, an untested connection — and the two OR together.
   *
   * `denied` is the one thing it does not combine with. A refused act never
   * started, so a refused control cannot be busy; if both arrive the refusal
   * wins whole, because the alternative is trading `aria-disabled` for a real
   * `disabled` and taking the sentence out of reach to show a spinner for an
   * act that is not running.
   */
  loading?: boolean
  ref?: Ref<HTMLButtonElement>
}

const sizeClass: Record<Size, string | undefined> = {
  default: undefined,
  sm: styles.sm,
  lg: styles.lg,
  icon: styles.icon,
  "icon-sm": styles.iconSm,
  "icon-lg": styles.iconLg,
}

/**
 * Whether a size's entire content is one glyph — the question `loading` asks to
 * decide between replacing the children and leading them. A total record rather
 * than a list of three names, so a seventh size cannot be added without
 * answering it.
 */
const glyphOnly: Record<Size, boolean> = {
  default: false,
  sm: false,
  lg: false,
  icon: true,
  "icon-sm": true,
  "icon-lg": true,
}

/**
 * The button's class recipe, for the one case a `<button>` is the wrong
 * element: a link that must look like a control. Nesting an anchor inside a
 * button (or the reverse) breaks keyboard and AT traversal, so the link takes
 * the classes instead of the component taking a polymorphic escape hatch.
 */
export function buttonClass(
  options: { variant?: Variant; size?: Size; className?: string } = {}
): string {
  const { variant = "default", size = "default", className } = options
  return cn(styles.button, styles[variant], sizeClass[size], className)
}

export function Button({
  variant = "default",
  size = "default",
  className,
  type = "button",
  denied,
  loading = false,
  disabled,
  title,
  onClick,
  children,
  ref,
  ...rest
}: ButtonProps) {
  const blocked = Boolean(denied)
  // Two different answers to "why is this not responding", and only one of them
  // can be true at a time: the click that would have started the act was
  // refused, so there is no act in flight to draw. The refusal wins whole —
  // including the focusability its sentence is reached through.
  const busy = loading && !blocked
  // Inside a kit tooltip the sentence is already on its way to the pointer, and
  // a native title beside it would deliver it twice in two different shapes.
  // The accessible name and the refused click are untouched either way.
  const nativeTitle = useInTooltip() ? undefined : (denied ?? title)
  const spinner = <Loader2 className={styles.spinner} aria-hidden="true" />

  return (
    <button
      type={type}
      ref={ref}
      data-test="button"
      data-variant={variant}
      data-size={size}
      /* Carries the sentence, not just the fact. CSS matches on the attribute
         regardless of its value, so the styling is unchanged — but a test, and
         anyone reading the DOM, can see *why* without opening a tooltip. */
      data-denied={denied || undefined}
      aria-disabled={blocked || undefined}
      /* Busy is a real `disabled`, not the `aria-disabled` above: there is
         nothing to explain and nothing to reach for, so dropping out of the
         tab order for the length of the act is the honest reading. */
      disabled={disabled || busy}
      aria-busy={busy || undefined}
      title={nativeTitle}
      onClick={(event: MouseEvent<HTMLButtonElement>) => {
        if (blocked) {
          event.preventDefault()
          return
        }
        onClick?.(event)
      }}
      className={cn(styles.button, styles[variant], sizeClass[size], className)}
      {...rest}
    >
      {busy ? (
        glyphOnly[size] ? (
          spinner
        ) : (
          <>
            {spinner}
            {children}
          </>
        )
      ) : (
        children
      )}
    </button>
  )
}
