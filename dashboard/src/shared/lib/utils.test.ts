import { describe, it, expect } from "vitest";
import { cn } from "./utils";

describe("cn", () => {
  it("merges class names using clsx + tailwind-merge", () => {
    const result = cn("text-red-500", "text-blue-500");
    expect(result).toBe("text-blue-500");
  });

  it("handles empty inputs", () => {
    const result = cn();
    expect(result).toBe("");
  });

  it("passes through strings unchanged", () => {
    const result = cn("flex gap-4 p-6");
    expect(result).toBe("flex gap-4 p-6");
  });

  it("handles conditional classes", () => {
    const isActive = true;
    const result = cn("base-class", isActive && "active");
    expect(result).toBe("base-class active");
  });
});
