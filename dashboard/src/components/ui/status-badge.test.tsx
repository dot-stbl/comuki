import { describe, it, expect } from "vitest";
import { render } from "@testing-library/react";
import { StatusBadge } from "./status-badge";

describe("StatusBadge", () => {
  it("renders the capitalized status as the default label", () => {
    const { getByText } = render(<StatusBadge status="running" />);
    expect(getByText("Running")).toBeInTheDocument();
  });

  it("renders custom children instead of the status label when provided", () => {
    const { getByText, queryByText } = render(
      <StatusBadge status="success">Done</StatusBadge>
    );
    expect(getByText("Done")).toBeInTheDocument();
    expect(queryByText("Success")).not.toBeInTheDocument();
  });

  it.each([
    ["running"],
    ["success"],
    ["failed"],
    ["waiting"],
    ["queued"],
    ["escalated"],
  ] as const)("renders a leading icon for status %s", (status) => {
    const { container } = render(<StatusBadge status={status} />);
    expect(container.querySelector("svg")).toBeInTheDocument();
  });

  it("merges a custom className onto the badge", () => {
    const { container } = render(
      <StatusBadge status="queued" className="extra-class" />
    );
    expect(container.firstChild).toHaveClass("extra-class");
  });

  it("renders the small size variant", () => {
    // Exercises the size === "sm" icon-size branch.
    const { container } = render(<StatusBadge status="running" size="sm" />);
    const icon = container.querySelector("svg");
    expect(icon).toHaveClass("size-2.5");
  });
});
