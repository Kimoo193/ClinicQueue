---
name: ClinicQueue
description: Clinical operations UI for smart clinic queues — teal trust, cool stone surfaces, bilingual RTL-ready.
colors:
  background: "#f7faf8"
  foreground: "#2a3a34"
  card: "#fbfcfb"
  primary: "#1f6b5c"
  primary-foreground: "#f7fcfa"
  secondary: "#e8f0eb"
  muted: "#e9f0eb"
  muted-foreground: "#4d6358"
  accent: "#e4f0ea"
  destructive: "#b33a2e"
  border: "#cfdad3"
  ring: "#3d8a78"
  success: "#2a6b52"
  warning-muted: "#f5ecd8"
  warning-foreground: "#5c4a28"
  info-muted: "#e8eef5"
  info-foreground: "#2f4560"
  app-wash-from: "#e8f3ee"
  app-wash-via: "#f4f5f0"
  app-wash-to: "#f2f0e8"
typography:
  body:
    fontFamily: "Geist Variable, Segoe UI, Tahoma, Geneva, Verdana, sans-serif"
    fontSize: "1rem"
    fontWeight: 400
    lineHeight: 1.5
  title:
    fontFamily: "Geist Variable, Segoe UI, Tahoma, Geneva, Verdana, sans-serif"
    fontSize: "1.5rem"
    fontWeight: 600
    lineHeight: 1.25
    letterSpacing: "-0.025em"
  label:
    fontFamily: "Geist Variable, Segoe UI, Tahoma, Geneva, Verdana, sans-serif"
    fontSize: "0.875rem"
    fontWeight: 400
    lineHeight: 1.4
rounded:
  sm: "0.3rem"
  md: "0.4rem"
  lg: "0.5rem"
spacing:
  sm: "8px"
  md: "16px"
  lg: "24px"
  section: "32px"
components:
  button-primary:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.primary-foreground}"
    rounded: "{rounded.lg}"
    padding: "8px 10px"
    height: "32px"
  button-primary-hover:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.primary-foreground}"
  button-outline:
    backgroundColor: "{colors.card}"
    textColor: "{colors.foreground}"
    rounded: "{rounded.lg}"
    padding: "8px 10px"
    height: "32px"
  field:
    backgroundColor: "{colors.card}"
    textColor: "{colors.foreground}"
    rounded: "{rounded.md}"
    padding: "10px 12px"
  card:
    backgroundColor: "{colors.accent}"
    textColor: "{colors.foreground}"
    rounded: "{rounded.lg}"
    padding: "16px"
  alert:
    backgroundColor: "#f8e9e7"
    textColor: "{colors.destructive}"
    rounded: "{rounded.md}"
    padding: "8px 12px"
---

# Design System: ClinicQueue

## 1. Overview

**Creative North Star: "The Clinic Floor Console"**

ClinicQueue looks like a calm clinical operations tool: cool teal-tinted surfaces, one deep teal for primary actions, and clear status colors for wait/go and errors. Density serves reception and doctor workflows; patient screens stay short and instructional. The system rejects purple SaaS gradients, cream-terracotta editorial kits, and decorative glass.

Identity lives in OKLCH tokens in `web/src/index.css` (canonical). Hex values in the frontmatter are sRGB approximations for tooling.

**Key Characteristics:**
- Restrained palette: teal primary ≤10% of surface; neutrals carry the rest
- Single sans family (Geist + Arabic-capable system fallbacks)
- Shared `cq-*` layout primitives across all role consoles
- Flat surfaces; depth via tonal wash and 1px dividers, not heavy shadows
- RTL-first structure (`start`/`end`, logical properties)

## 2. Colors

Cool clinical wash with a deep teal action color. Semantic success / warning / info support queue instructions without competing with primary.

### Primary
- **Deep Clinical Teal** (`oklch(0.4 0.085 170)`): Primary buttons, active nav, focus ring family. Rarity is intentional.

### Neutral
- **Cool Clinical Wash** (`--background`, `--app-wash-*`): Page shell gradient from soft teal into stone/amber-tinted cool neutrals — atmosphere, not cream paper.
- **Ink** (`--foreground` / `oklch(0.22 0.025 160)`): Body and titles.
- **Secondary Ink** (`--muted-foreground` / `oklch(0.45 0.02 160)`): Supporting copy; tuned for ≥4.5:1 on background.
- **Divider** (`--divider`): Soft teal-ink hairlines for sections and lists.

### Semantic
- **Destructive** — errors and alerts (`cq-alert`)
- **Warning** — “head to clinic” / read-only owner banner
- **Info** — “wait at home”
- **Success** — positive status messages (e.g. export ready)

**The One Accent Rule.** Primary teal is for actions and current selection only — never large decorative fills.

## 3. Typography

**Display Font:** Geist Variable (with Segoe UI / Tahoma for Arabic coverage)
**Body Font:** Same stack
**Label/Mono Font:** System mono for clinic IDs and membership GUIDs (`cq-field-mono`)

**Character:** One product sans. Fixed rem steps, not fluid display clamps. Tight hierarchy for operational density.

### Hierarchy
- **Brand / page title** (600, ~1.5–1.75rem): `cq-brand-mark`, `cq-page-title`
- **Section title** (600, 1rem): `cq-section-title`
- **Body** (400, 0.875–1rem): forms, list rows, instructions
- **Label** (400, 0.875rem): field labels
- **Metric / queue number** (600, tabular-nums, up to 1.5rem): temporary/real numbers

**The Product Type Rule.** No display serifs, no fluid hero clamps, no uppercase eyebrows on every section. Uppercase is reserved for tiny status chips only.

## 4. Elevation

Flat by default. Depth comes from the app wash gradient, card tint (`--surface-tint`), and hairline borders. Shadows appear only for focused skip-link / elevated focus affordances — not resting cards.

### Shadow Vocabulary
- **Focus lift** (`focus:shadow-sm` on skip link): momentary keyboard affordance only.

**The Flat-By-Default Rule.** No multi-layer card shadows. If it looks like a 2014 material card, remove the shadow.

## 5. Components

### Buttons
- **Shape:** Gently rounded (`rounded-lg` / ~8px)
- **Primary:** Teal fill, light foreground; hover at ~80% opacity
- **Outline / Ghost:** For secondary and inactive nav
- **Sizes:** Prefer default (≥32px) for primary actions; `sm` only in dense toolbars — bump to default on touch-critical reception actions when possible
- **Focus:** `ring-3` with `--ring`

### Cards / Containers
- **Corner Style:** `rounded-lg`
- **Background:** `cq-card` uses tinted surface, not nested white cards
- **Border:** `border-divider`
- **Internal Padding:** 16px (`p-4`)

### Inputs / Fields
- **Style:** `cq-field` / `cq-field-mono` — card fill, input border, 10px vertical padding for touch
- **Focus:** Border + ring using `--ring`
- **Error:** `cq-alert` above or beside the form; `aria-invalid` on controls when wired

### Navigation
- **Style:** `cq-nav` — bordered card strip with wrap; active = primary, inactive = ghost
- **Mobile:** Wrap; keep targets tappable

### Alerts & Instructions
- **Error:** `cq-alert`
- **Go / Wait:** `cq-instruction-go` / `cq-instruction-wait` (never color alone — always text)
- **Owner read-only:** `cq-banner-warning`

### Lists
- **Style:** `cq-list` / `cq-list-plain` — divide with `--divider`, no card-per-row unless the row is a patient appointment card

## 6. Do's and Don'ts

### Do:
- **Do** use `cq-*` primitives from `index.css` for new screens so patient/reception/doctor/admin stay one vocabulary.
- **Do** keep Arabic RTL and English LTR parity; prefer logical properties (`start`/`end`, `ps`/`pe`).
- **Do** make queue numbers and wait/go instructions the visual peak of patient views.
- **Do** use semantic status tokens (`destructive`, `warning`, `info`, `success`) instead of raw Tailwind `red-700` / `emerald-800`.

### Don't:
- **Don't** use purple-on-white / indigo SaaS gradients or “AI tool” marketing chrome.
- **Don't** use warm cream/sand/terracotta editorial defaults as the body background.
- **Don't** use hero-metric templates, identical icon card grids, glassmorphism, or gradient text.
- **Don't** invent a second button or field style per feature folder.
- **Don't** put side-stripe accent borders on cards or alerts.
- **Don't** hard-code `stone-200` / `emerald-900/10` dividers when `border-divider` exists.
