import { describe, it, expect, afterEach } from "vitest";
import { renderHook } from "@testing-library/react";
import { useIsMobile } from "./use-mobile";

function setViewport(width: number) {
  Object.defineProperty(window, "innerWidth", {
    writable: true,
    configurable: true,
    value: width,
  });
}

afterEach(() => {
  setViewport(1024);
});

describe("useIsMobile", () => {
  it("returns true below the 768px breakpoint", () => {
    setViewport(500);
    const { result } = renderHook(() => useIsMobile());
    expect(result.current).toBe(true);
  });

  it("returns false at or above the 768px breakpoint", () => {
    setViewport(1024);
    const { result } = renderHook(() => useIsMobile());
    expect(result.current).toBe(false);
  });
});
