// Visual audit script: iterate all Storybook stories, screenshot each, capture errors.
// Output: dashboard/.audit/screenshots/*.png + dashboard/.audit/report.json + summary.txt

import { chromium } from "@playwright/test";
import { writeFileSync, mkdirSync } from "node:fs";
import { join } from "node:path";

const STORYBOOK_URL = "http://localhost:6006";
const OUTPUT_DIR = "dashboard/.audit";
const VIEWPORT = { width: 1440, height: 900 };

type StoryReport = {
  id: string;
  title: string;
  screenshot: string;
  consoleErrors: string[];
  pageErrors: string[];
};

async function main() {
  mkdirSync(OUTPUT_DIR, { recursive: true });
  mkdirSync(join(OUTPUT_DIR, "screenshots"), { recursive: true });

  const browser = await chromium.launch({
    headless: false,
    args: [
      "--no-sandbox",
      "--disable-setuid-sandbox",
      "--disable-dev-shm-usage",
      "--disable-gpu",
    ],
    timeout: 60000,
  });
  const page = await browser.newPage({ viewport: VIEWPORT });

  const errorsByStory = new Map<string, { console: string[]; page: string[] }>();
  let currentStory = "_initial";

  page.on("console", (msg) => {
    if (msg.type() === "error") {
      const e = errorsByStory.get(currentStory) ?? { console: [], page: [] };
      e.console.push(msg.text());
      errorsByStory.set(currentStory, e);
    }
  });
  page.on("pageerror", (err) => {
    const e = errorsByStory.get(currentStory) ?? { console: [], page: [] };
    e.page.push(err.message);
    errorsByStory.set(currentStory, e);
  });

  // Load Storybook overview
  console.log(`Navigating to ${STORYBOOK_URL}...`);
  await page.goto(STORYBOOK_URL, { waitUntil: "networkidle", timeout: 30000 });
  await page.waitForTimeout(2000);

  await page.screenshot({
    path: join(OUTPUT_DIR, "screenshots/00-overview.png"),
    fullPage: false,
  });

  // Collect all story items from sidebar (Storybook 8 explorer)
  const storyItems = await page.evaluate(() => {
    const items = document.querySelectorAll('[id^="storybook-explorer-treeitem-"]');
    return Array.from(items)
      .map((item) => {
        const id = (item as HTMLElement).id.replace("storybook-explorer-treeitem-", "");
        const text = item.textContent?.trim() ?? "";
        return { id, text };
      })
      .filter((item) => item.id.includes("--")); // only leaf stories
  });

  console.log(`Found ${storyItems.length} stories`);

  const reports: StoryReport[] = [];

  for (const item of storyItems) {
    currentStory = item.id;
    errorsByStory.set(item.id, { console: [], page: [] });

    try {
      const url = `${STORYBOOK_URL}/?path=/story/${encodeURIComponent(item.id)}`;
      await page.goto(url, { waitUntil: "networkidle", timeout: 15000 });
      await page.waitForTimeout(1500);

      const screenshotPath = join(OUTPUT_DIR, `screenshots/${item.id}.png`);
      await page.screenshot({ path: screenshotPath, fullPage: false });

      const errs = errorsByStory.get(item.id)!;
      reports.push({
        id: item.id,
        title: item.text,
        screenshot: screenshotPath,
        consoleErrors: errs.console,
        pageErrors: errs.page,
      });

      if (errs.console.length || errs.page.length) {
        console.log(
          `✗ ${item.id}: ${errs.console.length} console, ${errs.page.length} page errors`,
        );
      } else {
        console.log(`✓ ${item.id}`);
      }
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e);
      console.log(`!! ${item.id}: ${msg}`);
      reports.push({
        id: item.id,
        title: item.text,
        screenshot: "",
        consoleErrors: [msg],
        pageErrors: [],
      });
    }
  }

  // Write report
  writeFileSync(join(OUTPUT_DIR, "report.json"), JSON.stringify(reports, null, 2));

  const failed = reports.filter((r) => r.consoleErrors.length > 0 || r.pageErrors.length > 0);
  writeFileSync(
    join(OUTPUT_DIR, "summary.txt"),
    [
      `Total stories: ${reports.length}`,
      `With errors: ${failed.length}`,
      `Without errors: ${reports.length - failed.length}`,
      ``,
      `Failures (${failed.length}):`,
      ...failed.map((r) => `  - ${r.id}: console=${r.consoleErrors.length} page=${r.pageErrors.length}`),
      ``,
      `Sample errors (first 10):`,
      ...failed.slice(0, 10).flatMap((r) => [
        `--- ${r.id} ---`,
        ...r.consoleErrors.slice(0, 3),
        ...r.pageErrors.slice(0, 3),
        ``,
      ]),
    ].join("\n"),
  );

  await browser.close();
  console.log(`\nReport: ${join(OUTPUT_DIR, "report.json")}`);
  console.log(`Summary: ${join(OUTPUT_DIR, "summary.txt")}`);
  console.log(`Screenshots: ${join(OUTPUT_DIR, "screenshots/")}`);
}

main().catch((e: unknown) => {
  console.error("FATAL:", e);
  process.exit(1);
});
