import { describe, it, expect } from "vitest";
import { render } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RunIdChip } from "./run-id-chip";

// navigator.clipboard is undefined in jsdom; install a resolving mock so the
// copy handler takes its success branch. (The native node clipboard, when
// present, is non-configurable and can't be spied reliably across this
// vitest/jsdom combo — so we assert the observable copied state instead.)
function mockClipboardResolves() {
  Object.defineProperty(navigator, "clipboard", {
    value: { writeText: () => Promise.resolve() },
    configurable: true,
    writable: true,
  });
}

describe("RunIdChip", () => {
  it("renders the id and exposes it as the button title", () => {
    const { getByRole, getByText } = render(<RunIdChip id="run_abc123" />);
    expect(getByRole("button")).toHaveAttribute("title", "run_abc123");
    expect(getByText("run_abc123")).toBeInTheDocument();
  });

  it("flips to the success state when the id is copied", async () => {
    mockClipboardResolves();
    const user = userEvent.setup();
    const { container, getByRole } = render(<RunIdChip id="run_t1" />);

    // Before click: only the Copy icon (no success color).
    expect(container.querySelector(".text-st-success")).not.toBeInTheDocument();

    await user.click(getByRole("button"));

    // After click: the Check icon carries the success-color class.
    expect(container.querySelector(".text-st-success")).toBeInTheDocument();
  });
});
