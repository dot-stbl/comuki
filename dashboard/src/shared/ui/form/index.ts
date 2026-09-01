/**
 * The kit's form controls — a composite primitive, in its own folder.
 *
 * Sources and Identity each built this set privately, twenty minutes apart,
 * because `shared/ui` had no labelled input to give them and the loose shadcn
 * files beside it are being strangled. Both folders called themselves promotion
 * candidates and both were right; this is the promotion. Where the two spellings
 * differed the more careful half won — Sources' three-tone `Notice`, its switch
 * and choice, Identity's `SecretValue` and `CopyButton`, and a `FormDialog` that
 * takes every footer arrangement either screen had asked for.
 *
 * Internals are private the way the data table's are: domains import from
 * `@/shared/ui` and nothing reaches past this file.
 */
export { ChoiceField, type ChoiceFieldProps, type ChoiceOption } from "./choice-field"
export { CopyButton, type CopyButtonProps } from "./copy-button"
export { Field, type FieldProps } from "./field"
export { FormDialog, type FormDialogProps } from "./form-dialog"
export { Notice, type NoticeProps } from "./notice"
export {
  NumberField,
  type NumberFieldProps,
} from "./number-field"
export { SecretValue, type SecretValueProps } from "./secret-value"
export {
  SelectField,
  type SelectFieldOption,
  type SelectFieldProps,
} from "./select-field"
export { SwitchField, type SwitchFieldProps } from "./switch-field"
export { TextField, type TextFieldProps } from "./text-field"
export { TextareaField, type TextareaFieldProps } from "./textarea-field"
