import type { ReactNode, TextareaHTMLAttributes } from "react"

import { cn } from "@/shared/lib/utils"

import { Field } from "./field"
import styles from "./form.module.css"
import { fieldDescriptionId } from "./ids"

export interface TextareaFieldProps
  extends Omit<
    TextareaHTMLAttributes<HTMLTextAreaElement>,
    "id" | "value" | "onChange"
  > {
  id: string
  label: string
  value: string
  onValueChange: (next: string) => void
  hint?: ReactNode
  error?: string | null
  /**
   * The two voices, made a prop because a textarea genuinely carries both.
   *
   * `prose` is something a person wrote and another person will read — a ticket
   * body — and it takes the interface voice. `code` is a value the product will
   * consume — an expression, a command — and it takes the data voice. Getting
   * this backwards is the defect the two-voices rule exists to catch.
   */
  voice?: "prose" | "code"
}

export function TextareaField({
  id,
  label,
  value,
  onValueChange,
  hint,
  error,
  voice = "prose",
  ...rest
}: TextareaFieldProps) {
  return (
    <Field id={id} label={label} hint={hint} error={error}>
      <textarea
        {...rest}
        id={id}
        className={cn(
          styles.control,
          voice === "code" ? styles.code : styles.area
        )}
        value={value}
        aria-invalid={error ? true : undefined}
        aria-describedby={hint || error ? fieldDescriptionId(id) : undefined}
        onChange={(event) => onValueChange(event.target.value)}
      />
    </Field>
  )
}
