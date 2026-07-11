import "@testing-library/jest-dom/vitest";
import { cleanup } from "@testing-library/react";
import { afterEach } from "vitest";

import i18n from "@/i18n";

afterEach(() => {
  cleanup();
});

void i18n.changeLanguage("en");
