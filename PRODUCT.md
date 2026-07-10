# Product

## Register

product

## Users

Clinic staff and patients in Arabic-first (RTL) and English clinic operations:

- **Patients** — book appointments, track live queue position (count-ahead), know when to leave home vs head to clinic; manage dependents and bookings across clinics.
- **Receptionists** — run the daily smart queue: confirm arrivals, call next, book on behalf, mark urgent, instant entry, restrict repeat absences.
- **Doctors** — pause/resume the queue in emergencies; record visits, diagnoses, and e-prescriptions for the patient being seen.
- **Clinic owners** — oversight of clinical content (read-only) and clinic operations.
- **Platform admins** — activate/suspend clinics, manage subscriptions, review analytics, export clinic data.

Context: busy clinic floors and phones; patients often on mobile; staff need dense, reliable controls under time pressure. Launch clinic is OB/GYN (Dr. Ghada Kamel); product is multi-tenant SaaS from day one.

## Product Purpose

ClinicQueue is a web-first smart clinic queue: count-based live position (not estimated wait time), so patients can wait at home and staff can run a trustworthy numbered queue with urgent interleave (default 2:1), instant entry, and emergency pause without losing place.

Success: no duplicate queue numbers under concurrency; patients trust “N ahead” and head-to-clinic guidance; reception can run the day without paper or phone chaos; clinical notes stay least-privilege.

## Brand Personality

Calm, clinical, trustworthy.

Voice is clear and operational — short instructions, status that can be acted on, no marketing fluff. Emotion: reassurance under uncertainty (patients) and control under pressure (staff).

## Anti-references

- Purple-on-white / indigo SaaS gradients and “AI tool” marketing chrome
- Warm cream/sand/terracotta editorial defaults
- Hero-metric dashboards, identical icon card grids, glassmorphism
- Decorative motion or display fonts in operational UI
- Dark neon “health tech” aesthetics

## Design Principles

1. **Trust the number** — Queue position and status are the product; make them the clearest thing on every patient and reception screen.
2. **Task over chrome** — Design serves the workflow; remove anything that doesn’t help book, confirm, call, pause, or record.
3. **Bilingual by default** — Arabic RTL is the default experience; English is first-class; layout and copy must work in both directions.
4. **Calm under pressure** — Dense where staff need density; quiet color; errors and emergencies are unmistakable without shouting.
5. **One vocabulary** — Same buttons, fields, alerts, and list patterns across patient, reception, doctor, and admin surfaces.

## Accessibility & Inclusion

- WCAG 2.2 AA target for text contrast, focus visibility, and form labeling
- Arabic (RTL, default) + English from day one; `dir`/`lang` must follow language
- Keyboard operable primary flows; visible focus rings; skip link to main content
- Respect `prefers-reduced-motion`
- Touch-friendly targets on patient and reception mobile use (≥44×44px where practical)
- Status and errors announced via semantic roles (`alert`, `status`); no color-only meaning for wait/go instructions
