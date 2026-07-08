import "@testing-library/jest-dom/vitest";
import { vi } from "vitest";

// matchMedia — not implemented in jsdom; needed by theme-provider (system
// theme resolution) and use-mobile (breakpoint query).
Object.defineProperty(window, "matchMedia", {
  writable: true,
  value: vi.fn().mockImplementation((query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
    addListener: vi.fn(),
    removeListener: vi.fn(),
    dispatchEvent: vi.fn(),
  })),
});

// Pointer-capture APIs — Radix UI primitives (DropdownMenu, Dialog, …) call
// these on trigger; jsdom doesn't implement them, which breaks open/click
// flows in component tests.
Element.prototype.hasPointerCapture = vi.fn(() => false);
Element.prototype.setPointerCapture = vi.fn();
Element.prototype.releasePointerCapture = vi.fn();

// scrollIntoView — also called by Radix; jsdom no-op.
Element.prototype.scrollIntoView = vi.fn();
