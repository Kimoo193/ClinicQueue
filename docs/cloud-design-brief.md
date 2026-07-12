# Cloud Design brief — ClinicQueue

Paste-ready pack for **Claude Design / Cloud Design / Figma AI** to regenerate every screen with one coherent **business-card (BC)** look.

**Visual decision (locked for this regen):** Business-card navy + teal — trust like a printed clinic card.  
**Note:** Repo `DESIGN.md` + `web/src/index.css` currently describe a *cool clinical teal* system. This brief is the intentional redesign target. After comps are approved, sync `DESIGN.md` / CSS tokens to match — do not leave two palettes in production.

**Also attach:** `PRODUCT.md`, this file, screenshots of current UI (optional but strongly recommended), and `frontend_prompts_playbook.md` phases 1–5 for per-screen acceptance detail.

---

## 1. Product in one paragraph

ClinicQueue is a **web-first, multi-tenant smart clinic queue**. Patients track **count-ahead** (never ETA), know when to wait at home vs head to clinic, and book across clinics. Reception runs confirm / call-next / absent / urgent / instant entry / day-close. Doctors pause the queue and record visits + e-prescriptions. Owners see reports; platform admins manage clinics and subscriptions. Arabic RTL is default; English is first-class. Launch clinic: OB/GYN (Dr. Ghada Kamel).

---

## 2. Brand & BC visual system

### North star

**“Printed clinic business card, brought to life.”** Deep navy as brand ink, teal only for secondary actions and calm status, large readable queue numbers, almost no chrome. Patient screens feel like holding the card; staff screens use the same ink at higher density.

### Tokens (canonical for this regen — no random hex in comps)

| Token | Hex | Use |
|-------|-----|-----|
| `--primary` | `#1B3A6B` | Brand, headers, primary actions |
| `--primary-lt` | `#2A5298` | Hover / lifted primary |
| `--teal` | `#5BB8C1` | Secondary actions, accents, live/active |
| `--teal-lt` | `#A8DDE2` | Soft fills, calm wait states |
| `--rose` | `#E8A0A0` | Warm accent — sparingly |
| `--success` | `#4CAF82` | Positive / calm confirmation |
| `--warning` | `#F5A623` | Head-to-clinic, pause, caution |
| `--danger` | `#E05C5C` | Urgent, emergency, destructive |
| `--bg` | `#F7FAFB` | Page wash |
| `--surface` | `#FFFFFF` | Surfaces |
| `--text` | `#1B3A6B` | Body / titles (navy ink) |
| `--muted` | `#6B8CAE` | Secondary copy |
| `--border` | `#E2EDF5` | Hairlines |

### Typography

- **Arabic:** Tajawal (generous line-height)
- **English:** Clean humanist sans (not Inter/Roboto as the hero identity; avoid serif display)
- Queue / metric numbers: tabular, heavy weight, unmistakable size on patient screens

### Layout rules

- Flat surfaces; depth via wash + 1px borders — not multi-layer card shadows
- Primary/navy and teal together ≤ ~15% of any viewport; neutrals carry the rest
- One composition per screen job; no marketing stat strips on operational UIs
- RTL-first: logical `start`/`end`; icons and actions flip correctly in Arabic

### Anti-patterns (reject in every frame)

- Purple / indigo SaaS gradients, “AI tool” chrome
- Warm cream + terracotta editorial kits
- Glassmorphism, neon health-tech dark mode
- Hero-metric icon-card grids, floating badges on heroes
- Raw clinic UUIDs anywhere in the UI
- ETA / “~12 min wait” language (product forbids it)

---

## 3. Roles & density

| Role | Device | Density | Emotional job |
|------|--------|---------|----------------|
| Patient | Phone-first | Sparse, instructional | Reassure; trust the number |
| Auth / entry | Phone + desktop | Calm brand | Instant clinic recognition |
| Reception | Desktop desk | Dense, scannable | Speed under pressure |
| Doctor | Desktop | Summary → drill-down | Control between patients |
| Owner | Desktop | Charts + insight lines | Clarity, not vanity dashboards |
| Admin | Desktop | Tables + confirmations | Platform control, safe suspend |

---

## 4. Hard product UI rules (must appear in designs)

| ID / rule | UI requirement |
|-----------|----------------|
| FR-025 | Show **N patients ahead** as a **count** — never time estimates |
| Threshold | Wait-at-home vs head-to-clinic from **clinic-configured** threshold (not hardcoded “4”) |
| Four patient states | `waitHome` · `headToClinic` · `yourTurn` · `paused` (+ reassure place is saved when paused) |
| FR-022 | Bookings: unmistakable that **real queue number is assigned on arrival / confirm** |
| FR-024 | Urgent is **visually distinct** (not text-only); 2:1 interleave is product behavior |
| FR-031 | Instant entry: **reason mandatory** + short why |
| FR-065 | Day-close: deposit no-shows auto carry-over called out |
| FR-047 | Repeated-absence warnings for staff |
| FR-102 | Admin suspend → clinic **read-only**, not deleted; confirm |
| Clinic context | Never ask users to paste a clinic UUID; slug / QR / picker by name only |
| i18n | Every string designed for **ar + en**; show RTL variant for hero + reception at minimum |
| A11y | Visible focus, ≥4.5:1 text, status not color-only, touch targets on patient/reception |

---

## 5. Screen checklist (regen order)

Approve **#1** before propagating the system. Name frames by route.

### Wave A — lock the system + hero

| # | Route | Frame name | One job |
|---|-------|------------|---------|
| 0 | — | `_DesignSystem` | Tokens, type, button/field/alert/list-row, 4 queue states |
| 1 | `/app/queue` | `PatientQueue` | Queue number + status instruction as visual peak; live pulse; type badge |

**PatientQueue states (required frames):** waitHome · headToClinic · yourTurn · paused · loading · empty · error  
**Variants:** `ar-RTL` + `en-LTR` for at least waitHome + yourTurn.

### Wave B — staff daily driver

| # | Route | Frame name | One job |
|---|-------|------------|---------|
| 2 | `/reception` | `ReceptionConsole` | Dense queue table + always-visible KPIs + confirm / call / absent; emergency pause separated |
| 3 | `/reception/day-close` | `ReceptionDayClose` | End-of-day summary; per-patient carry/cancel; irreversible confirm with counts |

### Wave C — patient + first impression

| # | Route | Frame name | One job |
|---|-------|------------|---------|
| 4 | `/app/bookings` | `PatientBookings` | Tabs + FR-022 callout; book / cancel / dependents |
| 5 | `/app/medical` | `PatientMedical` | Calm clinical; **allergies prominent**; visit + Rx |
| 6 | `/login` | `Login` | Trustworthy first impression |
| 7 | `/register` | `Register` | Same brand system |
| 8 | `/r/:clinicSlug` | `ClinicEntry` | **That clinic’s** name/logo/colors dominant (QR land) |
| 9 | `/find-clinic` | `FindClinic` | Helpful, not a dead end |
| 10 | `/forbidden` | `Forbidden` | Plain explain + way out |
| 11 | `/` | `Landing` | Brand-first; not a dashboard |

### Wave D — remaining consoles

| # | Route | Frame name | One job |
|---|-------|------------|---------|
| 12 | `/doctor` | `DoctorConsole` | Today counts → queue oversight → visit/Rx; pause/resume |
| 13 | `/owner/reports` | `OwnerReports` | Charts + one insight line each; invite reception |
| 14 | `/owner/settings` | `OwnerSettings` | Queue rules (threshold, urgent ratio, absence), branding, QR/join link |
| 15 | `/admin` | `AdminConsole` | Clinics list, suspend confirm, QR/onboarding link, export |
| 16 | `/choose-clinic` | `ChooseClinic` | Multi-clinic pick by **name** only (never UUID) |

### Removed / not for this phase

| Item | Action |
|------|--------|
| Bookings “Find a clinic” search (read-only dead end) | **Removed** from `/app/bookings` — use `/find-clinic` + QR instead |
| `/dev/queue-states` | Keep **DEV-only** — not a customer screen |
| Waiting-room TV | Phase 2+ — not in this regen wave |

### Dev reference (optional)

| Route | Note |
|-------|------|
| `/dev/queue-states` | Existing gallery — good screenshot source for state parity |

---

## 6. Paste-ready master prompt

Copy everything in the block below into Cloud Design (attach this file + `PRODUCT.md` + screenshots).

```text
You are redesigning ClinicQueue — a multi-tenant clinic queue SaaS (patient, reception, doctor, owner, admin).

VISUAL DIRECTION (locked): Business-card navy + teal.
- primary #1B3A6B, primary-lt #2A5298, teal #5BB8C1, teal-lt #A8DDE2
- rose #E8A0A0 (sparingly), success #4CAF82, warning #F5A623, danger #E05C5C
- bg #F7FAFB, surface #FFFFFF, text #1B3A6B, muted #6B8CAE, border #E2EDF5
- Arabic: Tajawal. English: clean humanist sans. Flat UI; hairline borders; no heavy shadows.
- North star: “printed clinic business card, brought to life.” Patient = sparse phone UI; reception/doctor = dense desk UI.

PRODUCT RULES (non-negotiable in every frame):
- Count-ahead only — NEVER ETA or minutes-wait.
- Patient states: wait at home / head to clinic / your turn / paused (reassure place saved).
- No clinic UUIDs in UI. Clinic branding on /r/:slug must feel unmistakable.
- Bookings must scream: real number assigned when reception confirms arrival.
- Urgent rows visually distinct. Instant-entry reason mandatory. Day-close irreversible with counts.
- Bilingual ar+en; RTL-first. Status never color-only.

ANTI-PATTERNS: purple/indigo SaaS, cream-terracotta editorial, glass, neon dark mode, hero-metric card grids, floating promo badges.

WORKFLOW:
1) Produce _DesignSystem (tokens + components + four queue states).
2) Produce ONLY PatientQueue (/app/queue) with all states + ar/en for key states. STOP for approval.
3) After approval, continue Wave B → C → D using the exact screen list in docs/cloud-design-brief.md. Same system, adapt density per role.
4) Name frames by route. Include loading / empty / error where the product would otherwise flash blank.
5) Export specs suitable for React + Tailwind token implementation (presentation layer only — no inventing new APIs).
```

### Follow-up prompt (after PatientQueue approved)

```text
PatientQueue is approved. Propagate the same BC navy+teal system.
Build Wave B next: ReceptionConsole (/reception) then ReceptionDayClose (/reception/day-close).
Reception = dense desktop desk UI: KPI strip, scannable table, urgent visually distinct, confirm/call/absent, mandatory instant-entry reason, emergency pause hard to misfire + full paused console state.
Day-close = summary, per-patient carry/cancel, FR-065 deposit no-show note, irreversible confirm with carry/notify counts.
Then stop for approval before Wave C.
```

---

## 7. Handoff to engineering

When comps are approved:

1. Update `DESIGN.md` frontmatter + prose to this BC system (retire competing clinical-teal story or mark it superseded).
2. Map tokens into `web/src/index.css` / Tailwind theme — **no hardcoded hex in components**.
3. Presentation-only pass: keep API, SignalR, auth, and query logic unchanged (`frontend_prompts_playbook.md` Phase B rule).
4. Verify with playbook checklists (i18n parity, RTL, loading/empty/error, no UUID leakage).
5. Re-run Vitest + Playwright smoke after visual pass.

---

## 8. Quick reference — current app routes

From `web/src/App.tsx`:

- Public: `/`, `/login`, `/register`, `/find-clinic`, `/choose-clinic`, `/r/:clinicSlug`
- Patient: `/app` → `/app/queue`, `/app/bookings`, `/app/medical`
- Reception: `/reception`, `/reception/day-close`
- Doctor: `/doctor`
- Owner: `/owner` → `/owner/reports`, `/owner/settings`
- Admin: `/admin`
- Other: `/forbidden`, `/dev/queue-states` (DEV only)
