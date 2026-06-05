import { test, expect } from "@playwright/test";

test.describe("Landing page smoke tests", () => {
  test("homepage loads and shows Comuki heading", async ({ page }) => {
    await page.goto("/");
    await expect(page.locator("h1")).toContainText("Comuki");
  });

  test("homepage shows dashboard scaffold card", async ({ page }) => {
    await page.goto("/");
    await expect(page.getByText("Dashboard scaffold")).toBeVisible();
  });

  test("homepage has no console errors", async ({ page }) => {
    const errors: string[] = [];
    page.on("console", (msg) => {
      if (msg.type() === "error") {
        errors.push(msg.text());
      }
    });
    await page.goto("/");
    await page.waitForLoadState("networkidle");
    expect(errors).toHaveLength(0);
  });
});
