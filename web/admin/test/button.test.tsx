import { afterEach, expect, test } from "bun:test";
import { cleanup, render, screen } from "@testing-library/react";
import { Button } from "../src/components/ui";

afterEach(cleanup);

/**
 * WebKit on Linux stopped painting the sign-in page for good when the primary button's
 * `hover:brightness-110` was animated while `disabled` toggled its opacity: 16 of 90 lockout runs
 * in the Playwright suite, and none once `filter` was left out of the transition
 * (docs/plan-phase-14.md, 2026-09-28). Tailwind's plain `transition` includes `filter`, so it must
 * not come back on a button that carries a filter.
 */
test("no button variant animates a filter", () => {
  for (const variant of ["primary", "secondary", "danger"] as const) {
    render(<Button variant={variant}>{variant}</Button>);
    const classes = screen.getByRole("button", { name: variant }).className.split(/\s+/);

    expect(classes).not.toContain("transition");
    expect(classes).not.toContain("transition-all");
    expect(classes.filter((name) => name.startsWith("transition-[") && name.includes("filter"))).toEqual([]);
    expect(classes).toContain("transition-[color,background-color,border-color,opacity,box-shadow]");
  }
});
