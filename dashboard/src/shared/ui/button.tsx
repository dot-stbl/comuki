import type { ButtonHTMLAttributes, MouseEvent, Ref } from "react"

import { cn } from "@/shared/lib/utils"

import { useInTooltip } from "./tooltip-context"

import styles from "./button.module.css"

type Variant =
  | "default"
  | "outline"
  | "secondary"
  | "ghost"
  | "destructive"
  | "link"

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
   * by either a pointer or a keyboard. Use `disabled` for *busy* and *invalid*;
   * use `denied` for *not yours*.
   */
  denied?: string | null
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
  title,
  onClick,
  ref,
  ...rest
}: ButtonProps) {
  const blocked = Boolean(denied)
  // Inside a kit tooltip the sentence is already on its way to the pointer, and
  // a native title beside it would deliver it twice in two different shapes.
  // The accessible name and the refused click are untouched either way.
  const nativeTitle = useInTooltip() ? undefined : (denied ?? title)

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
    />
  )
}
