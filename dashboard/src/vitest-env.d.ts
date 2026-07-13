// Makes tsc (the `tsc -b` build step) aware of @testing-library/jest-dom
// matchers (toBeInTheDocument, toHaveClass, …) on vitest's `expect`.
// vitest.setup.ts imports the same path for runtime registration.
import "@testing-library/jest-dom/vitest";
