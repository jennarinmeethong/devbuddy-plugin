import { afterEach, beforeEach, describe, expect, test } from "bun:test";
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { Glob } from "bun";
import { act, cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import type { ReactElement } from "react";
import { Hint, TourButton, type TourStep } from "../src/components/Guide";
import { HINTS, TOURS } from "../src/guide/content";
import { SessionProvider } from "../src/api/session";
import { App } from "../src/App";
import { forgetTokens } from "../src/api/client";
import { fakeServer, PROJECT, RECORD, WORKSPACE, type FakeServer } from "./server";

/**
 * The tours and the "?" hints.
 *
 * A tour step names an element by its `data-tour` value, so renaming one on a screen would leave
 * the step pointing at nothing, and the tour would skip it without anybody noticing. The first test
 * holds every name a tour uses to one a screen carries.
 */

const SRC = join(import.meta.dir, "..", "src");

function sourceOfEveryScreen(): string {
  let all = "";
  for (const file of new Glob("**/*.tsx").scanSync(SRC)) {
    all += readFileSync(join(SRC, file), "utf8");
  }
  return all;
}

describe("the tour content", () => {
  test("every step points at something a screen carries", () => {
    const source = sourceOfEveryScreen();
    const targets = Object.values(TOURS)
      .flat()
      .map((step: TourStep) => step.target)
      .filter((target): target is string => target !== undefined);

    expect(targets.length).toBeGreaterThan(40);

    const missing = targets.filter(
      (target) =>
        !source.includes(`tour="${target}"`) &&
        !source.includes(`data-tour="${target}"`) &&
        !source.includes(`"${target}" : undefined`),
    );

    expect(missing).toEqual([]);
  });

  test("every hint is used somewhere", () => {
    const source = sourceOfEveryScreen();
    const unused = Object.keys(HINTS).filter((name) => !source.includes(`HINTS.${name}`));

    expect(unused).toEqual([]);
  });

  test("every screen declares a tour", () => {
    const routes = new Glob("routes/*.tsx").scanSync(SRC);
    const without = [...routes].filter((file) => {
      const source = readFileSync(join(SRC, file), "utf8");
      return !source.includes("usePageTour(TOURS.") && !source.includes("<TourButton steps={TOURS.");
    });

    expect(without).toEqual([]);
  });
});

describe("a tour", () => {
  afterEach(cleanup);

  const steps: TourStep[] = [
    { title: "Welcome", body: "First paragraph.\n\nSecond paragraph." },
    { target: "here", title: "Something here", body: "It is here." },
    { target: "gone", title: "Something hidden", body: "Not on this page." },
    { target: "there", title: "Something there", body: "It is there." },
  ];

  function Page() {
    return (
      <>
        <TourButton steps={steps} />
        <p data-tour="here">here</p>
        <p data-tour="there">there</p>
      </>
    );
  }

  test("walks the steps, skipping one whose element is not on the page", () => {
    render(<Page />);

    fireEvent.click(screen.getByRole("button", { name: "Tour" }));

    const dialog = screen.getByRole("dialog", { name: "Welcome" });
    expect(within(dialog).getByText("Step 1 of 3")).toBeDefined();
    expect(within(dialog).getByText("Second paragraph.")).toBeDefined();
    expect((within(dialog).getByRole("button", { name: "Back" }) as HTMLButtonElement).disabled).toBe(true);

    fireEvent.click(within(dialog).getByRole("button", { name: "Next" }));
    expect(screen.getByRole("dialog", { name: "Something here" })).toBeDefined();

    fireEvent.click(screen.getByRole("button", { name: "Next" }));
    expect(screen.getByRole("dialog", { name: "Something there" })).toBeDefined();
    expect(screen.getByText("Step 3 of 3")).toBeDefined();
    expect(screen.queryByText("Something hidden")).toBeNull();

    fireEvent.click(screen.getByRole("button", { name: "Finish" }));
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  test("Escape ends it, and the arrow keys move through it", () => {
    render(<Page />);

    fireEvent.click(screen.getByRole("button", { name: "Tour" }));
    fireEvent.keyDown(document, { key: "ArrowRight" });
    expect(screen.getByRole("dialog", { name: "Something here" })).toBeDefined();

    fireEvent.keyDown(document, { key: "ArrowLeft" });
    expect(screen.getByRole("dialog", { name: "Welcome" })).toBeDefined();

    fireEvent.keyDown(document, { key: "Escape" });
    expect(screen.queryByRole("dialog")).toBeNull();
  });
});

describe("a hint", () => {
  afterEach(cleanup);

  test("opens a dialog that explains, and closes on Escape or its button", () => {
    render(<Hint topic={{ title: "A topic", body: "What it is.\n\nWhat it is not." }} />);

    const button = screen.getByRole("button", { name: "Explain: A topic" });
    fireEvent.click(button);

    const dialog = screen.getByRole("dialog", { name: "A topic" });
    expect(within(dialog).getByText("What it is not.")).toBeDefined();

    fireEvent.keyDown(document, { key: "Escape" });
    expect(screen.queryByRole("dialog")).toBeNull();

    fireEvent.click(button);
    fireEvent.click(within(screen.getByRole("dialog")).getByRole("button", { name: "Close" }));
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  test("inside a form, opening it submits nothing", () => {
    let submitted = false;
    render(
      <form
        onSubmit={(event) => {
          event.preventDefault();
          submitted = true;
        }}
      >
        <Hint topic={{ title: "A topic", body: "Body." }} />
      </form>,
    );

    fireEvent.click(screen.getByRole("button", { name: "Explain: A topic" }));

    expect(submitted).toBe(false);
    expect(screen.getByRole("dialog", { name: "A topic" })).toBeDefined();
  });
});

describe("on the real screens", () => {
  let server: FakeServer;

  function mount(path: string): ReactElement {
    const queries = new QueryClient({
      defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false } },
    });

    return (
      <QueryClientProvider client={queries}>
        <MemoryRouter initialEntries={[path]}>
          <SessionProvider>
            <App />
          </SessionProvider>
        </MemoryRouter>
      </QueryClientProvider>
    );
  }

  beforeEach(() => {
    sessionStorage.clear();
    server = fakeServer();
  });

  afterEach(() => {
    cleanup();
    forgetTokens();
    server.restore();
  });

  test("the sign-in page offers its tour before anybody signs in", async () => {
    render(mount("/"));

    fireEvent.click(await screen.findByRole("button", { name: "Tour" }));

    expect(screen.getByRole("dialog", { name: "Signing in" })).toBeDefined();
  });

  test("the header starts the tour of the screen below it", async () => {
    sessionStorage.setItem("devbuddy.refresh", "refresh");
    render(mount(`/w/${WORKSPACE}`));

    await screen.findByRole("link", { name: "Alpha" });
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "Tour" }));
    });

    const dialog = screen.getByRole("dialog", { name: "The workspace" });
    expect(within(dialog).getByText(/^Step 1 of/)).toBeDefined();

    fireEvent.click(within(dialog).getByRole("button", { name: "Next" }));
    expect(screen.getByRole("dialog", { name: "The menu" })).toBeDefined();
  });

  test("a record's approval explains why it names a hash", async () => {
    sessionStorage.setItem("devbuddy.refresh", "refresh");
    render(mount(`/w/${WORKSPACE}/p/${PROJECT}/records/${RECORD}`));

    fireEvent.click(await screen.findByRole("button", { name: "Explain: Why the approval names a hash" }));

    expect(screen.getByRole("dialog", { name: "Why the approval names a hash" })).toBeDefined();
  });

  test("a project screen's button starts that screen's tour", async () => {
    sessionStorage.setItem("devbuddy.refresh", "refresh");
    render(mount(`/w/${WORKSPACE}/p/${PROJECT}/records`));

    await screen.findByRole("button", { name: "Tour" });
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "Tour" }));
    });

    expect(screen.getByRole("dialog", { name: "The review queue" })).toBeDefined();
  });
});
