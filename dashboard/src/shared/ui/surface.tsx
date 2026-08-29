import type { HTMLAttributes, ReactNode } from "react"

import { cn } from "@/shared/lib/utils"

export type SurfaceVariant = "default" | "raised" | "sunk" | "transparent"

export interface SurfaceProps extends HTMLAttributes<HTMLDivElement> {
  /**
   * Visual tier of the surface. Each variant maps to a pair of shadcn tokens
   * (background + foreground) so the surface reads correctly in both light
   * and dark themes without hardcoded colors.
   *
   * - `default`  — `bg-card` / `text-card-foreground`   (panels, cards)
   * - `raised`   — `bg-popover` / `text-popover-foreground` (popovers, dialogs)
   * - `sunk`     — `bg-muted` / `text-muted-foreground` (wells, inputs)
   * - `transparent` — no background, inherits parent foreground
   */
  variant?: SurfaceVariant
  /** Render with a 1px border. Defaults to `true` to match design-system "panels are quiet wells". */
  border?: boolean
  /** Optional corner radius scale (Tailwind). Defaults to `md` (6px) per design-system `--r-sm`. */
  rounded?: "none" | "sm" | "md" | "lg" | "xl"
  children?: ReactNode
}

const variantClass: Record<SurfaceVariant, string> = {
  default: "bg-card text-card-foreground",
  raised: "bg-popover text-popover-foreground",
  sunk: "bg-muted text-muted-foreground",
  transparent: "bg-transparent text-foreground",
}

const roundedClass: Record<NonNullable<SurfaceProps["rounded"]>, string> = {
  none: "rounded-none",
  sm: "rounded-sm",
  md: "rounded-md",
  lg: "rounded-lg",
  xl: "rounded-xl",
}

export function Surface({
  className,
  variant = "default",
  border = true,
  rounded = "md",
  children,
  ...props
}: SurfaceProps) {
  return (
    <div
      className={cn(
        roundedClass[rounded],
        variantClass[variant],
        border && "border border-border",
        className,
      )}
      {...props}
    >
      {children}
    </div>
  )
}
