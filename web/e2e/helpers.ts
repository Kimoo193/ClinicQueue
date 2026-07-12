import { expect, type Page } from "@playwright/test";

export const PASSWORD = "Test@1234";

export const accounts = {
  admin: "admin@clinicqueue.app",
  ghada: "ghada@clinic.test",
  doctor: "doctor@clinic.test",
  reception: "reception@clinic.test",
  patient1: "patient1@clinic.test",
  patient2: "patient2@clinic.test",
  patient3: "patient3@clinic.test",
  patient4: "patient4@clinic.test",
  urgent: "urgent@clinic.test",
  multi: "multi@clinic.test",
  newbie: "newbie@clinic.test",
} as const;

/** Force English UI before any navigation (app defaults to Arabic). */
export async function useEnglish(page: Page): Promise<void> {
  await page.addInitScript(() => {
    window.localStorage.setItem("i18nextLng", "en");
  });
}

export async function useArabic(page: Page): Promise<void> {
  await page.addInitScript(() => {
    window.localStorage.setItem("i18nextLng", "ar");
  });
}

async function fillLoginForm(page: Page, email: string): Promise<void> {
  const user = page.locator('input[autocomplete="username"]');
  const pass = page.locator('input[autocomplete="current-password"]');
  await expect(user).toBeVisible({ timeout: 20_000 });

  for (let attempt = 1; attempt <= 5; attempt++) {
    await user.fill(email);
    await pass.fill(PASSWORD);
    await page.locator('form button[type="submit"]').click();

    const rateLimited = page.getByText(/too many requests|كثرة الطلبات|429/i);
    if (await rateLimited.isVisible().catch(() => false)) {
      await page.waitForTimeout(15_000);
      continue;
    }
    return;
  }
}

export async function loginAs(
  page: Page,
  email: string,
  options?: { expectChooseClinic?: boolean; arabic?: boolean },
): Promise<void> {
  if (options?.arabic) {
    await useArabic(page);
  } else {
    await useEnglish(page);
  }
  await page.goto("/login");
  // Ensure English even if detector lagged: toggle when dir is still rtl.
  if (!options?.arabic) {
    const dir = await page.locator("html").getAttribute("dir");
    if (dir === "rtl") {
      const switchLang = page.getByRole("button", {
        name: /switch language|english|إنجليزي/i,
      });
      if (await switchLang.first().isVisible().catch(() => false)) {
        await switchLang.first().click();
      }
    }
  }
  await fillLoginForm(page, email);

  if (options?.expectChooseClinic) {
    await expect(page.locator("button.cq-clinic-pick").first()).toBeVisible({
      timeout: 20_000,
    });
    return;
  }

  await page.waitForURL((url) => !url.pathname.startsWith("/login"), {
    timeout: 20_000,
  });
}

/** Assert no raw UUID is visible in the main document text. */
export async function assertNoRawClinicUuid(page: Page): Promise<void> {
  const body = await page.locator("body").innerText();
  const uuid =
    /\b[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}\b/i;
  expect(body, "raw clinic UUID must not appear in UI").not.toMatch(uuid);
}

export async function assertNoTimeEstimate(page: Page): Promise<void> {
  const body = (await page.locator("body").innerText()).toLowerCase();
  const banned = [
    "minute wait",
    "minutes wait",
    "estimated wait",
    "approx. wait",
    "eta:",
  ];
  for (const phrase of banned) {
    expect(body, `must not show time estimate (“${phrase}”)`).not.toContain(
      phrase,
    );
  }
}
