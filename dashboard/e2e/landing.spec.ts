import { test, expect } from "@playwright/test";

// The root route redirects to /components (the component showcase).
// The old assertions (h1 "Comuki", "Dashboard scaffold") targeted a landing
// page that no longer exists — these test what the app actually renders.

test.describe("Showcase smoke tests", () => {
  test("root redirects to /components and shows the Comuki brand", async ({
    page,
  }) => {
    await page.goto("/");

    await expect(page).toHaveURL(/\/components$/);
    await expect(page.getByText("Comuki").first()).toBeVisible();
  });

  test("showcase renders its first section", async ({ page }) => {
    await page.goto("/components");

    // Section 3.1 "Buttons" is the first heading in the showcase.
    await expect(
      page.getByRole("heading", { name: "Buttons" }).first()
    ).toBeVisible();
  });

  test("showcase loads with no console errors", async ({ page }) => {
    const errors: string[] = [];
    page.on("console", (message) => {
      if (message.type() === "error") {
        errors.push(message.text());
      }
    });

    await page.goto("/components");
    await page.waitForLoadState("networkidle");

    expect(errors).toHaveLength(0);
  });
});
