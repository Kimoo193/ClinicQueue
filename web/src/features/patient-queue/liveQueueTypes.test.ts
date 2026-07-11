import { describe, expect, it } from "vitest";

import {
  parseAppointmentType,
  resolveQueueVisualState,
} from "./liveQueueTypes";

describe("resolveQueueVisualState", () => {
  it("prioritizes paused over other signals", () => {
    expect(
      resolveQueueVisualState({
        status: "Inside",
        instruction: "yourTurn",
        paused: true,
      }),
    ).toBe("paused");
  });

  it("treats Inside as your turn", () => {
    expect(
      resolveQueueVisualState({
        status: "Inside",
        instruction: "headToClinic",
      }),
    ).toBe("yourTurn");
  });

  it("maps threshold instructions", () => {
    expect(
      resolveQueueVisualState({
        status: "Present",
        instruction: "waitHome",
      }),
    ).toBe("waitHome");
    expect(
      resolveQueueVisualState({
        status: "Present",
        instruction: "headToClinic",
      }),
    ).toBe("headToClinic");
  });

  it("falls back to pending", () => {
    expect(resolveQueueVisualState({ status: "Booked" })).toBe("pending");
  });
});

describe("parseAppointmentType", () => {
  it("defaults to Normal", () => {
    expect(parseAppointmentType(undefined)).toBe("Normal");
    expect(parseAppointmentType(null)).toBe("Normal");
    expect(parseAppointmentType("Normal")).toBe("Normal");
  });

  it("detects Urgent case-insensitively", () => {
    expect(parseAppointmentType("Urgent")).toBe("Urgent");
    expect(parseAppointmentType("urgent")).toBe("Urgent");
  });
});
