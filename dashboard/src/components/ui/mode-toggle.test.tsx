import { describe, it, expect, beforeEach } from "vitest";
import { render } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ThemeProvider } from "@/components/theme-provider";
import { ModeToggle } from "./mode-toggle";

function renderWithStoredTheme(theme: "dark" | "light" | "system") {
  localStorage.setItem("theme", theme);
  return render(
    <ThemeProvider defaultTheme="system">
      <ModeToggle />
    </ThemeProvider>
  );
}

describe("ModeToggle", () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it("renders an accessible toggle button", () => {
    const { getByRole } = renderWithStoredTheme("dark");
    expect(
      getByRole("button", { name: /toggle theme/i })
    ).toBeInTheDocument();
  });

  // Render once per theme so every icon branch (Sun/Moon/Monitor) executes.
  it.each([
    ["dark"],
    ["light"],
    ["system"],
  ] as const)("mounts under the %s theme without errors", (theme) => {
    const { container } = renderWithStoredTheme(theme);
    expect(container.querySelector("svg")).toBeInTheDocument();
  });

  it("switches theme when each menu item is chosen", async () => {
    const user = userEvent.setup();

    for (const choice of ["Light", "Dark", "System"] as const) {
      const { getByRole, getByText, unmount } = renderWithStoredTheme("dark");
      await user.click(getByRole("button", { name: /toggle theme/i }));
      await user.click(getByText(choice));
      const expected = choice.toLowerCase();
      expect(localStorage.getItem("theme")).toBe(expected);
      unmount();
    }
  });
});
