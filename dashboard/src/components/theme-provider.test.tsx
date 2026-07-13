import { describe, it, expect, beforeEach } from "vitest";
import { renderHook, act } from "@testing-library/react";
import { ThemeProvider, useTheme } from "./theme-provider";

const wrapper =
  (defaultTheme: "dark" | "light" | "system" = "system") =>
  ({ children }: { children: React.ReactNode }) =>
    <ThemeProvider defaultTheme={defaultTheme}>{children}</ThemeProvider>;

beforeEach(() => {
  localStorage.clear();
  document.documentElement.classList.remove("dark", "light");
});

describe("ThemeProvider", () => {
  it("falls back to defaultTheme when nothing is stored", () => {
    const { result } = renderHook(() => useTheme(), {
      wrapper: wrapper("dark"),
    });
    expect(result.current.theme).toBe("dark");
  });

  it("reads the stored theme from localStorage", () => {
    localStorage.setItem("theme", "light");
    const { result } = renderHook(() => useTheme(), { wrapper: wrapper() });
    expect(result.current.theme).toBe("light");
  });

  it("setTheme persists to localStorage and updates the value", () => {
    const { result } = renderHook(() => useTheme(), { wrapper: wrapper() });
    act(() => result.current.setTheme("dark"));
    expect(result.current.theme).toBe("dark");
    expect(localStorage.getItem("theme")).toBe("dark");
  });

  it("applies the resolved theme class to documentElement", () => {
    renderHook(() => useTheme(), { wrapper: wrapper("dark") });
    expect(document.documentElement.classList.contains("dark")).toBe(true);
  });

  it("throws when useTheme is used outside a provider", () => {
    expect(() => renderHook(() => useTheme())).toThrow(/within a ThemeProvider/);
  });

  it("toggles dark↔light on a plain 'd' keydown", () => {
    const { result } = renderHook(() => useTheme(), { wrapper: wrapper("dark") });
    act(() => {
      window.dispatchEvent(new KeyboardEvent("keydown", { key: "d" }));
    });
    expect(result.current.theme).toBe("light");
  });

  it("reacts to a same-tab storage event for its key", () => {
    const { result } = renderHook(() => useTheme(), { wrapper: wrapper("dark") });
    act(() => {
      window.dispatchEvent(
        new StorageEvent("storage", {
          key: "theme",
          newValue: "light",
          storageArea: localStorage,
        })
      );
    });
    expect(result.current.theme).toBe("light");
  });
});
