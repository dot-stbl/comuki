import "@testing-library/jest-dom"

/* The real translation instance, `en` active: component tests resolve the
   real locale files, so a key typo or a missing namespace renders a key
   path and fails a text assertion loudly instead of passing against a
   mock catalogue (`dashboard-i18n` D6). */

import "@/shared/i18n"

/* jsdom implements neither of these, and the duty screen uses both: the stage
   river reads `matchMedia` to honour reduced motion and calls `scrollIntoView`
   to bring the pinch into the scrollport. Without stubs the first component
   test that renders it throws before it asserts anything. */

if (!window.matchMedia) {
  window.matchMedia = ((query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addEventListener: () => {},
    removeEventListener: () => {},
    addListener: () => {},
    removeListener: () => {},
    dispatchEvent: () => false,
  })) as unknown as typeof window.matchMedia
}

if (!Element.prototype.scrollIntoView) {
  Element.prototype.scrollIntoView = () => {}
}
