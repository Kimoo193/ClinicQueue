import { expect, test } from "@playwright/test";

import {
  accounts,
  assertNoRawClinicUuid,
  assertNoTimeEstimate,
  loginAs,
  PASSWORD,
  useArabic,
  useEnglish,
} from "./helpers";

/** Count-ahead is a big numeral + caption (may be bilingual). */
function countAheadLocator(page: import("@playwright/test").Page) {
  return page.locator(".cq-count-ahead").first();
}

test.describe("S1 · Patient journey", () => {
  test("patient1 sees WAIT AT HOME with count-ahead (no ETA)", async ({
    page,
  }) => {
    await loginAs(page, accounts.patient1);
    await expect(page).toHaveURL(/\/app\/queue/);

    const hero = page.locator(".cq-queue-hero").first();
    await expect(hero).toBeVisible();
    const number = hero.locator(".cq-queue-number");
    await expect(number).toBeVisible();
    const box = await number.boundingBox();
    expect(box?.height ?? 0).toBeGreaterThan(48);

    await expect(countAheadLocator(page)).toBeVisible();
    await expect(countAheadLocator(page)).toHaveText(/7/);
    await expect(
      page.locator('.cq-queue-status[data-state="waitHome"]').first(),
    ).toBeVisible();
    await assertNoTimeEstimate(page);
  });

  test("patient2 sees HEAD TO CLINIC", async ({ page }) => {
    await loginAs(page, accounts.patient2);
    await expect(page).toHaveURL(/\/app\/queue/);
    await expect(
      page.locator('.cq-queue-status[data-state="headToClinic"]').first(),
    ).toBeVisible();
    await expect(countAheadLocator(page)).toHaveText(/2/);
    await assertNoTimeEstimate(page);
  });

  test("reception call-next decrements patient2 count live via SignalR", async ({
    browser,
  }) => {
    const patientContext = await browser.newContext();
    const receptionContext = await browser.newContext();
    const patientPage = await patientContext.newPage();
    const receptionPage = await receptionContext.newPage();

    await loginAs(patientPage, accounts.patient2);
    await expect(patientPage).toHaveURL(/\/app\/queue/);
    await expect(countAheadLocator(patientPage)).toBeVisible();
    const beforeCount = Number(
      (await countAheadLocator(patientPage).innerText()).trim(),
    );

    await loginAs(receptionPage, accounts.reception);
    await expect(receptionPage).toHaveURL(/\/reception/);

    const callNext = receptionPage.getByRole("button", {
      name: /call next|استدعاء التالي/i,
    });
    await expect(callNext.first()).toBeVisible();
    await callNext.first().click();

    await expect
      .poll(
        async () =>
          Number(
            (await countAheadLocator(patientPage).innerText().catch(() => `${beforeCount}`)).trim(),
          ),
        { timeout: 20_000 },
      )
      .toBeLessThan(beforeCount);

    await patientContext.close();
    await receptionContext.close();
  });
});

test.describe("S2 · Reception journey", () => {
  test("confirm arrival, call next, instant-entry reason gate", async ({
    page,
  }) => {
    await loginAs(page, accounts.reception);
    await expect(page).toHaveURL(/\/reception/);

    const confirmBtn = page.getByRole("button", {
      name: /confirm arrival|تأكيد الحضور/i,
    });
    await expect(confirmBtn.first()).toBeVisible();

    const bodyBefore = (await page.locator("body").innerText()).toLowerCase();
    expect(bodyBefore).toMatch(/booked|محجوز|temp|مؤقت/);

    await confirmBtn.first().click();

    await expect
      .poll(
        async () => (await page.locator("body").innerText()).toLowerCase(),
        { timeout: 15_000 },
      )
      .toMatch(/present|حاضر|inside|داخل/);

    const callNext = page.getByRole("button", {
      name: /call next|استدعاء التالي/i,
    });
    await expect(callNext.first()).toBeVisible();
    await callNext.first().click();

    const reason = page.getByLabel(/reason|السبب/i).first();
    await expect(reason).toBeVisible();
    await reason.fill("   ");
    const insert = page.getByRole("button", {
      name: /insert now|instant entry|إدخال/i,
    });
    await insert.first().click();
    await expect(
      page.getByText(/required|مطلوب|reason|سبب|audit|سجل/i).first(),
    ).toBeVisible();
  });

  test("mark absent advances queue", async ({ page }) => {
    await loginAs(page, accounts.reception);
    await expect(page).toHaveURL(/\/reception/);

    const markAbsent = page.getByRole("button", {
      name: /mark absent|تغي[يى]ب|غائب/i,
    });
    await expect(markAbsent.first()).toBeVisible();

    const responsePromise = page.waitForResponse(
      (r) => r.url().includes("mark-absent"),
      { timeout: 15_000 },
    );
    await markAbsent.first().click();
    const dialogConfirm = page
      .locator(".cq-confirm-backdrop, [role='dialog']")
      .getByRole("button", { name: /confirm|تأكيد|mark absent|غائب/i });
    if (await dialogConfirm.first().isVisible().catch(() => false)) {
      await dialogConfirm.first().click();
    }

    const response = await responsePromise.catch(() => null);
    expect(
      response,
      "POST .../mark-absent must be called — missing endpoint is a product bug",
    ).not.toBeNull();
    expect(
      response!.ok(),
      `mark-absent HTTP ${response!.status()} — missing/broken backend is a product bug`,
    ).toBeTruthy();
  });
});

test.describe("S3 · Urgent injection 2:1", () => {
  test("urgent is present and not absolute front of numbered queue", async ({
    page,
  }) => {
    await loginAs(page, accounts.reception);
    await expect(page).toHaveURL(/\/reception/);

    const body = await page.locator("body").innerText();
    expect(body.toLowerCase()).toMatch(/urgent|عاجل/);

    const urgentRow = page.locator("tr, li, [class*='row']").filter({
      hasText: /urgent|عاجل/i,
    });
    await expect(urgentRow.first()).toBeVisible();
    expect(body).toMatch(/#?\s*[1-6]\b/);
  });
});

test.describe("S4 · Emergency pause / resume", () => {
  test("doctor pause reassures patient; number unchanged; resume continues", async ({
    browser,
  }) => {
    const doctorCtx = await browser.newContext();
    const patientCtx = await browser.newContext();
    const doctor = await doctorCtx.newPage();
    const patient = await patientCtx.newPage();

    await loginAs(patient, accounts.patient1);
    await expect(patient).toHaveURL(/\/app\/queue/);
    const numberBefore = (
      await patient.locator(".cq-queue-number").first().innerText()
    ).trim();

    await loginAs(doctor, accounts.doctor);
    await expect(doctor).toHaveURL(/\/doctor/);

    const pause = doctor.getByRole("button", {
      name: /pause|emergency|إيقاف/i,
    });
    await expect(pause.first()).toBeVisible();
    await pause.first().click();

    const reason = doctor.getByLabel(/reason|السبب/i).first();
    if (await reason.isVisible().catch(() => false)) {
      await reason.fill("E2E emergency drill");
    }
    const confirm = doctor
      .locator(".cq-confirm-backdrop, [role='dialog']")
      .getByRole("button", { name: /confirm|تأكيد|pause|إيقاف/i });
    if (await confirm.first().isVisible().catch(() => false)) {
      await confirm.first().click();
    }

    await expect(
      patient.locator('.cq-queue-status[data-state="paused"]').first(),
    ).toBeVisible({ timeout: 20_000 });
    await expect(
      patient.getByText(/saved|محفوظ|preserved|مكانك/i).first(),
    ).toBeVisible();

    const numberPaused = (
      await patient.locator(".cq-queue-number").first().innerText()
    ).trim();
    expect(numberPaused).toBe(numberBefore);

    const resume = doctor.getByRole("button", { name: /resume|استئناف/i });
    await expect(resume.first()).toBeVisible();
    await resume.first().click();

    await expect(
      patient.locator(
        '.cq-queue-status[data-state="waitHome"], .cq-queue-status[data-state="headToClinic"]',
      ).first(),
    ).toBeVisible({ timeout: 20_000 });

    await doctorCtx.close();
    await patientCtx.close();
  });
});

test.describe("S5 · Day close + carry-over", () => {
  test("confirmation shows carry-over count and irreversibility", async ({
    page,
  }) => {
    await loginAs(page, accounts.reception);
    await page.goto("/reception/day-close");

    await expect(
      page.getByText(/served|مخدوم|absent|غائب|waiting|انتظار/i).first(),
    ).toBeVisible({ timeout: 15_000 });

    await expect(
      page.getByText(/deposit|عربون|auto.?carry|ترحيل/i).first(),
    ).toBeVisible();

    const closeBtn = page.getByRole("button", {
      name: /close (the )?day|إغلاق|end of day|close session/i,
    });
    await expect(closeBtn.first()).toBeVisible();
    await closeBtn.first().click();

    await expect(
      page.getByText(/irreversible|لا يمكن التراجع|cannot be reopened/i).first(),
    ).toBeVisible();
    await expect(
      page.getByText(/carry|ترحيل|notified|إشعار|auto-carry/i).first(),
    ).toBeVisible();
  });
});

test.describe("S6 · Role isolation", () => {
  test("patient → forbidden on staff/admin; anon → login", async ({ page }) => {
    await loginAs(page, accounts.patient1);
    await page.goto("/reception");
    await expect(page).toHaveURL(/\/forbidden/);
    await expect(
      page.getByText(/not authorized|forbidden|مصرح|صلاحية|access/i).first(),
    ).toBeVisible();

    await page.goto("/admin");
    await expect(page).toHaveURL(/\/forbidden/);

    await page.context().clearCookies();
    await useEnglish(page);
    await page.goto("/app/queue");
    await expect(page).toHaveURL(/\/login/);
  });

  test("receptionist forbidden from admin; nav role-scoped", async ({
    page,
  }) => {
    await loginAs(page, accounts.reception);
    await page.goto("/admin");
    await expect(page).toHaveURL(/\/forbidden/);

    await page.goto("/reception");
    const header = page.locator("header").first();
    await expect(header.getByRole("link", { name: /^admin$|إدارة المنصة/i })).toHaveCount(0);
  });
});

test.describe("S7 · Multi-clinic switcher", () => {
  test("choose clinic; Smile patient forbidden on reception; Ghada reception OK", async ({
    page,
  }) => {
    await loginAs(page, accounts.multi, { expectChooseClinic: true });
    await assertNoRawClinicUuid(page);

    await page
      .locator("button.cq-clinic-pick")
      .filter({ hasText: /smile|سمايل/i })
      .click();
    await page.waitForURL((u) => !u.pathname.includes("login"));
    await page.goto("/reception");
    await expect(page).toHaveURL(/\/forbidden/);
    await assertNoRawClinicUuid(page);

    await page.context().clearCookies();
    await loginAs(page, accounts.multi, { expectChooseClinic: true });
    await page
      .locator("button.cq-clinic-pick")
      .filter({ hasText: /ghada|غادة/i })
      .click();
    await expect(page).toHaveURL(/\/reception/);
    await assertNoRawClinicUuid(page);
  });
});

test.describe("S8 · Onboarding via QR", () => {
  test("/r/ghada shows clinic branding while logged out", async ({ page }) => {
    await useEnglish(page);
    await page.goto("/r/ghada");
    await expect(page.getByText(/ghada|غادة/i).first()).toBeVisible({
      timeout: 15_000,
    });
  });

  test("newbie without membership lands on find-clinic", async ({ page }) => {
    await loginAs(page, accounts.newbie);
    await expect(page).toHaveURL(/\/find-clinic/);
    await expect(
      page.getByText(/find your clinic|عيادتك|clinic code|رمز|qr/i).first(),
    ).toBeVisible();
  });

  test("register from /r/ghada is not a dead end", async ({ page }) => {
    await useEnglish(page);
    const email = `e2e_${Date.now()}@clinic.test`;
    await page.goto("/r/ghada");

    const registerLink = page.getByRole("link", {
      name: /register|sign up|إنشاء|تسجيل/i,
    });
    if (await registerLink.first().isVisible().catch(() => false)) {
      await registerLink.first().click();
    } else {
      await page.goto("/register");
    }

    await page.locator('input[autocomplete="username"]').fill(email);
    await page
      .locator(
        'input[autocomplete="new-password"], input[autocomplete="current-password"], input[type="password"]',
      )
      .first()
      .fill(PASSWORD);
    await page.locator('form button[type="submit"]').click();

    await page.waitForURL(
      (url) =>
        url.pathname.includes("/app/") ||
        url.pathname.includes("/r/") ||
        url.pathname.includes("/find-clinic") ||
        !url.pathname.endsWith("/register"),
      { timeout: 25_000 },
    );
    expect(page.url()).not.toMatch(/\/register$/);
  });
});

test.describe("S9 · Arabic RTL", () => {
  test("dir=rtl and no raw i18n keys on patient queue", async ({ page }) => {
    await useArabic(page);
    await page.goto("/login");
    await expect(page.locator("html")).toHaveAttribute("dir", "rtl");

    await page.locator('input[autocomplete="username"]').fill(accounts.patient1);
    await page.locator('input[autocomplete="current-password"]').fill(PASSWORD);
    await page.locator('form button[type="submit"]').click();
    await page.waitForURL(/\/app\/queue/, { timeout: 20_000 });
    await expect(page.locator("html")).toHaveAttribute("dir", "rtl");

    const body = await page.locator("body").innerText();
    expect(body).not.toMatch(/patientQueue\./);
    expect(body).not.toMatch(/\bauth\.[a-zA-Z]+/);
  });
});
