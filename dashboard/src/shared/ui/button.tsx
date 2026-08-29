import type { ButtonHTMLAttributes, Ref } from "react"

import { cn } from "@/shared/lib/utils"

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

export function Button({
  variant = "default",
  size = "default",
  className,
  type = "button",
  ref,
  ...rest
}: ButtonProps) {
  return (
    <button
      type={type}
      ref={ref}
      data-test="button"
      data-variant={variant}
      data-size={size}
      className={cn(styles.button, styles[variant], sizeClass[size], className)}
      {...rest}
    />
  )
}
