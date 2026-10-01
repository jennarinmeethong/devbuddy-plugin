import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useId,
  useLayoutEffect,
  useRef,
  useState,
  type ReactNode,
} from "react";
import { createPortal } from "react-dom";
import { t } from "../i18n";
import { Button, Icon } from "./ui";

/**
 * A guided tour of the screen, and the small "?" that explains one part of it.
 *
 * A tour is a list of steps, each naming an element by its `data-tour` attribute. A step whose
 * element is not on the page is skipped rather than shown pointing at nothing: the screens hide
 * what the caller's role would be refused, so a viewer's tour is shorter than an administrator's,
 * and that is the tour being accurate rather than incomplete. A step with no target is shown in the
 * middle of the screen.
 *
 * Titles and bodies are English keys marked with `m` where they are written, and translated here
 * when they render, so switching language changes the tour too.
 *
 * Both dialogs are portalled to the body. A "?" sits inside a paragraph, a legend or a table cell,
 * where a dialog is not valid markup, and the tour's button sits in the header, whose
 * `backdrop-filter` makes it the containing block of anything `fixed` inside it, so an overlay
 * rendered there would cover the header and nothing else.
 *
 * Positions are set through the style object, which React writes through the CSSOM. The content
 * security policy has no `'unsafe-inline'` for styles, and that refuses a `style` attribute in
 * markup, not a property set from script, so nothing here needs the policy changed.
 */

export interface TourStep {
  /** The `data-tour` value of the element the step describes. None: shown in the middle. */
  target?: string;
  title: string;
  body: string;
}

export interface Topic {
  title: string;
  body: string;
}

const TourContext = createContext<{ steps: TourStep[] | null; setSteps: (steps: TourStep[] | null) => void }>({
  steps: null,
  setSteps: () => {},
});

/** Holds the tour of whatever screen is showing, so the header's button can start it. */
export function TourProvider({ children }: { children: ReactNode }) {
  const [steps, setSteps] = useState<TourStep[] | null>(null);

  return <TourContext.Provider value={{ steps, setSteps }}>{children}</TourContext.Provider>;
}

/** Declares this screen's tour. The list should be a constant, so it is registered once. */
export function usePageTour(steps: TourStep[]): void {
  const { setSteps } = useContext(TourContext);

  useEffect(() => {
    setSteps(steps);
    return () => setSteps(null);
  }, [steps, setSteps]);
}

/**
 * The button that starts a tour: this screen's, from `usePageTour`, or the one it is given, for a
 * screen outside the workspace frame. Renders nothing when there is no tour to start.
 */
export function TourButton({ steps }: { steps?: TourStep[] }) {
  const registered = useContext(TourContext).steps;
  const tour = steps ?? registered;
  const [open, setOpen] = useState(false);

  if (!tour || tour.length === 0) {
    return null;
  }

  return (
    <>
      <Button aria-haspopup="dialog" onClick={() => setOpen(true)}>
        <span aria-hidden="true" className="grid size-4 place-items-center rounded-full border border-current text-[0.6rem] font-bold">
          i
        </span>
        {t("Tour")}
      </Button>
      {open ? (
        <Tour steps={tour} onClose={() => setOpen(false)} />
      ) : null}
    </>
  );
}

function find(target: string | undefined): HTMLElement | null {
  if (!target || typeof document === "undefined") {
    return null;
  }

  return document.querySelector<HTMLElement>(`[data-tour="${target}"]`);
}

/** On the page and able to be seen: a closed drawer's links are in the page but not visible. */
function visible(element: HTMLElement): boolean {
  if (element.getClientRects().length === 0) {
    return false;
  }

  for (let node: HTMLElement | null = element; node; node = node.parentElement) {
    const style = getComputedStyle(node);
    if (style.visibility === "hidden" || style.display === "none") {
      return false;
    }
  }

  return true;
}

interface Box {
  top: number;
  left: number;
  width: number;
  height: number;
}

const GAP = 12;
const MARGIN = 16;

function Tour({ steps, onClose }: { steps: TourStep[]; onClose: () => void }) {
  // Decided once, when the tour starts: a step that appears halfway through, because the person
  // clicked something, would make the count change under them.
  const [shown] = useState(() =>
    steps.filter((step) => {
      if (!step.target) {
        return true;
      }

      const element = find(step.target);
      return element !== null && visible(element);
    }),
  );
  const [index, setIndex] = useState(0);
  const [box, setBox] = useState<Box | null>(null);
  const card = useRef<HTMLDivElement>(null);
  const [cardHeight, setCardHeight] = useState(0);
  const titleId = useId();

  const step = shown[index];
  const last = index === shown.length - 1;

  const measure = useCallback(() => {
    const element = find(step?.target);
    if (!element) {
      setBox(null);
      return;
    }

    const rect = element.getBoundingClientRect();
    setBox({ top: rect.top, left: rect.left, width: rect.width, height: rect.height });
  }, [step]);

  useLayoutEffect(() => {
    find(step?.target)?.scrollIntoView?.({ block: "center", inline: "nearest" });
    measure();
  }, [measure, step]);

  useLayoutEffect(() => {
    setCardHeight(card.current?.offsetHeight ?? 0);
  }, [index, box]);

  useEffect(() => {
    window.addEventListener("resize", measure);
    window.addEventListener("scroll", measure, true);
    return () => {
      window.removeEventListener("resize", measure);
      window.removeEventListener("scroll", measure, true);
    };
  }, [measure]);

  // Back to whatever started the tour, normally its button, when it ends.
  useEffect(() => {
    const before = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    return () => before?.focus();
  }, []);

  useEffect(() => {
    card.current?.focus();
  }, [index]);

  useEffect(() => {
    function key(event: KeyboardEvent) {
      if (event.key === "Escape") {
        event.preventDefault();
        onClose();
      } else if (event.key === "ArrowRight") {
        setIndex((current) => Math.min(current + 1, shown.length - 1));
      } else if (event.key === "ArrowLeft") {
        setIndex((current) => Math.max(current - 1, 0));
      }
    }

    document.addEventListener("keydown", key);
    return () => document.removeEventListener("keydown", key);
  }, [onClose, shown.length]);

  if (!step) {
    return null;
  }

  const width = typeof window === "undefined" ? 1024 : window.innerWidth;
  const height = typeof window === "undefined" ? 768 : window.innerHeight;
  const narrow = width < 640;
  const cardWidth = Math.min(384, width - MARGIN * 2);

  let position: { top?: number; left?: number; bottom?: number };
  if (narrow) {
    // A phone: the card sits at the bottom, where it covers least of what it points at.
    position = { left: MARGIN, bottom: MARGIN };
  } else if (!box) {
    position = { top: Math.max(MARGIN, (height - cardHeight) / 2), left: (width - cardWidth) / 2 };
  } else {
    const below = box.top + box.height + GAP;
    const above = box.top - GAP - cardHeight;
    const top =
      below + cardHeight <= height - MARGIN
        ? below
        : above >= MARGIN
          ? above
          : Math.max(MARGIN, height - MARGIN - cardHeight);
    const left = Math.min(Math.max(MARGIN, box.left), width - MARGIN - cardWidth);
    position = { top, left };
  }

  return createPortal(
    <div className="fixed inset-0 z-50">
      {/* Holds the page still while the tour talks about it. */}
      <div className="absolute inset-0" aria-hidden="true" onClick={(event) => event.stopPropagation()} />

      {box ? (
        <div
          aria-hidden="true"
          className="pointer-events-none fixed rounded-xl ring-3 ring-[var(--color-active)] shadow-[0_0_0_9999px_rgba(18,50,77,0.45)] transition-[top,left,width,height] duration-200"
          style={{ top: box.top - 6, left: box.left - 6, width: box.width + 12, height: box.height + 12 }}
        />
      ) : (
        <div aria-hidden="true" className="pointer-events-none fixed inset-0 bg-[rgba(18,50,77,0.45)]" />
      )}

      <div
        ref={card}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        tabIndex={-1}
        className="fixed space-y-3 rounded-xl border border-[var(--color-line)] bg-[var(--color-surface)] p-4 shadow-panel focus:outline-none"
        style={{ ...position, width: cardWidth }}
      >
        <div className="flex items-start justify-between gap-3">
          <div>
            <p className="text-xs font-semibold tracking-wide text-[var(--color-accent-bright)]">
              {t("Step {current} of {total}", { current: index + 1, total: shown.length })}
            </p>
            <h2 id={titleId} className="text-base font-semibold">
              {t(step.title)}
            </h2>
          </div>
          <button
            type="button"
            aria-label={t("End the tour")}
            onClick={onClose}
            className="grid size-8 shrink-0 place-items-center rounded-lg text-[var(--color-muted)] hover:bg-[var(--color-hover)]"
          >
            <Icon name="close" className="size-4" />
          </button>
        </div>

        <Paragraphs text={t(step.body)} />

        <div className="flex items-center justify-between gap-2 pt-1">
          <Button disabled={index === 0} onClick={() => setIndex(index - 1)}>
            {t("Back")}
          </Button>
          {last ? (
            <Button variant="primary" onClick={onClose}>
              {t("Finish")}
            </Button>
          ) : (
            <Button variant="primary" onClick={() => setIndex(index + 1)}>
              {t("Next")}
            </Button>
          )}
        </div>
      </div>
    </div>,
    document.body,
  );
}

/** Text written with blank lines between paragraphs, shown as paragraphs. */
function Paragraphs({ text }: { text: string }) {
  return (
    <div className="space-y-2 text-sm leading-relaxed">
      {text.split(/\n\s*\n/).map((paragraph, index) => (
        <p key={index} className="whitespace-pre-line">
          {paragraph}
        </p>
      ))}
    </div>
  );
}

/**
 * A dialog over the page. Escape and the close button end it, focus goes into it when it opens and
 * back to whatever opened it when it closes.
 */
export function Modal({ title, onClose, children }: { title: string; onClose: () => void; children: ReactNode }) {
  const panel = useRef<HTMLDivElement>(null);
  const titleId = useId();

  useEffect(() => {
    const before = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    panel.current?.focus();

    function key(event: KeyboardEvent) {
      if (event.key === "Escape") {
        event.preventDefault();
        onClose();
      }
    }

    document.addEventListener("keydown", key);
    return () => {
      document.removeEventListener("keydown", key);
      before?.focus();
    };
  }, [onClose]);

  return createPortal(
    <div className="fixed inset-0 z-50 grid place-items-center p-4">
      <div aria-hidden="true" className="absolute inset-0 bg-[rgba(18,50,77,0.45)]" onClick={onClose} />
      <div
        ref={panel}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        tabIndex={-1}
        className="relative max-h-[85vh] w-full max-w-lg overflow-y-auto rounded-xl border border-[var(--color-line)] bg-[var(--color-surface)] shadow-panel focus:outline-none"
      >
        <header className="flex items-center justify-between gap-3 border-b border-[var(--color-line)] bg-[var(--color-soft)] px-5 py-3">
          <h2 id={titleId} className="text-base font-semibold">
            {title}
          </h2>
          <button
            type="button"
            aria-label={t("Close")}
            onClick={onClose}
            className="grid size-8 shrink-0 place-items-center rounded-lg text-[var(--color-muted)] hover:bg-[var(--color-hover)]"
          >
            <Icon name="close" className="size-4" />
          </button>
        </header>
        <div className="p-5">{children}</div>
      </div>
    </div>,
    document.body,
  );
}

/**
 * The small "?" beside something that is easy to misread. It opens a dialog that explains it in
 * full: what it is, what it changes, and what it does not.
 */
export function Hint({ topic }: { topic: Topic }) {
  const [open, setOpen] = useState(false);
  const title = t(topic.title);
  const close = useCallback(() => setOpen(false), []);

  return (
    <>
      <button
        type="button"
        aria-label={t("Explain: {topic}", { topic: title })}
        aria-haspopup="dialog"
        onClick={(event) => {
          event.preventDefault();
          event.stopPropagation();
          setOpen(true);
        }}
        className={[
          "inline-grid size-5 shrink-0 place-items-center rounded-full border border-[var(--color-line-strong)]",
          "bg-[var(--color-surface)] align-middle text-[0.7rem] font-bold text-[var(--color-accent-bright)]",
          "hover:bg-[var(--color-hover)]",
        ].join(" ")}
      >
        ?
      </button>
      {open ? (
        <Modal title={title} onClose={close}>
          <Paragraphs text={t(topic.body)} />
        </Modal>
      ) : null}
    </>
  );
}
