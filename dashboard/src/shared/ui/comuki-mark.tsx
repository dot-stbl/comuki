import type { Ref, SVGProps } from "react"

export interface ComukiMarkProps extends SVGProps<SVGSVGElement> {
  ref?: Ref<SVGSVGElement>
}

/**
 * The Comuki mark: a freight container, open on the loading side, marked on the
 * face. It is the product's own object — a worker container is a function of
 * `(brief + code) → (diff + report)` that is torn down after one run.
 *
 * The viewBox is cropped to the artwork (422×310 inside the 500×500 source), so
 * the element's box *is* the glyph: set a height and the width follows. Fill is
 * `currentColor`, so the mark takes the colour of whatever it sits in.
 */
export function ComukiMark({ ref, ...rest }: ComukiMarkProps) {
  return (
    <svg
      ref={ref}
      viewBox="39 95 422 310"
      fill="currentColor"
      aria-hidden="true"
      focusable="false"
      {...rest}
    >
      <path d="M59.3967 148.985L246.132 95.5454C248.033 94.9994 250.028 94.8614 251.987 95.1402L436.841 121.596C443.534 122.582 449.652 125.939 454.085 131.059C458.517 136.178 460.971 142.719 461 149.496V350.503C460.971 357.28 458.517 363.822 454.085 368.941C449.652 374.06 443.534 377.417 436.841 378.403L251.987 404.824C251.329 404.927 250.665 404.986 250 405C248.691 405.003 247.389 404.819 246.132 404.454L59.3967 351.014C53.5399 349.315 48.3889 345.764 44.7127 340.891C41.0366 336.018 39.0326 330.085 39 323.977V176.022C39.0326 169.914 41.0366 163.981 44.7127 159.108C48.3889 154.235 53.5399 150.684 59.3967 148.985ZM306.267 235.909H334.4V135.405L264.067 125.33V374.669L334.4 364.594V264.091H306.267C302.536 264.091 298.958 262.606 296.32 259.963C293.682 257.321 292.2 253.737 292.2 250C292.2 246.263 293.682 242.678 296.32 240.036C298.958 237.393 302.536 235.909 306.267 235.909ZM432.867 149.496L362.533 139.421V235.909H390.667C394.397 235.909 397.975 237.393 400.613 240.036C403.251 242.678 404.733 246.263 404.733 250C404.733 253.737 403.251 257.321 400.613 259.963C397.975 262.606 394.397 264.091 390.667 264.091H362.533V360.578L432.867 350.503V149.496ZM235.933 372.221V127.778L67.1333 176.022V323.977L235.933 372.221Z" />
    </svg>
  )
}
