# Contract: Notifications Module (Internal Dispatch Interface)

The Notifications module has no direct public HTTP surface for sending — every
notification is triggered by another module persisting an `OutboxNotification`
row in the same transaction as its state change (constitution Principle III).
This is the internal contract every producing module MUST follow.

## Producer contract (any module → Outbox)

Any module that changes state that a patient/staff member needs to know about
MUST, within the same database transaction:

```text
OutboxNotification {
  ClinicId
  RecipientUserId
  Type: BookingConfirmation | HeadToClinic | YourTurn | Pause | Resume | CarryOver
  Payload: { ...type-specific fields, e.g. temporaryNumber, countAhead, reason }
}
```

| Trigger | Type | FR(s) |
|---|---|---|
| Appointment booked | `BookingConfirmation` | FR-070 |
| Count-ahead crosses threshold | `HeadToClinic` | FR-071 |
| Receptionist calls patient | `YourTurn` | FR-072 |
| Session paused | `Pause` | FR-051, FR-073 |
| Session resumed | `Resume` | FR-053, FR-073 |
| Appointment carried over | `CarryOver` | FR-063, FR-074 |

## Dispatcher contract (Hangfire worker → channels)

The worker polls `Pending` rows (~every 10s) and calls a channel-agnostic
`INotificationDispatcher.Dispatch(OutboxNotification)`:

1. Attempt SignalR push to `queue` hub group `clinic:{ClinicId}` if the
   recipient has a live connection.
2. Attempt Web Push (VAPID) if the recipient has granted browser permission.
3. On failure, retry up to 3× with exponential backoff (FR-076); after
   exhausting retries, mark `Failed` and raise an operational alert (FR-077).
4. [Phase 2 slot, not built now] FCM channel — the interface MUST accept a new
   channel implementation without changing producer-side code (constitution
   Principle VI).

**Guarantee**: no notification is ever silently dropped — it is always either
`Delivered` or `Failed`-with-alert (SC-009).
