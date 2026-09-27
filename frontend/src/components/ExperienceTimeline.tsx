import { useEffect, useRef, useState, type PointerEvent as ReactPointerEvent } from "react";
import type { DraftExperience } from "../api";

export type ExperienceDatePatch = {
  startDate?: string | null;
  endDate?: string | null;
  isCurrent?: boolean;
  include?: boolean;
};

type ExperienceTimelineProps = {
  experiences: DraftExperience[];
  onChangeExperience?: (id: string, patch: ExperienceDatePatch) => void;
  onSelectExperience?: (id: string) => void;
};

type TimelineRow = {
  id: string;
  label: string;
  sublabel: string;
  startMs: number | null;
  endMs: number | null;
  isCurrent: boolean;
  include: boolean;
};

type DragState = {
  id: string;
  edge: "start" | "end";
  originStartMs: number;
  originEndMs: number;
  startMs: number;
  endMs: number;
  isCurrent: boolean;
};

const MS_PER_DAY = 86_400_000;
const MS_PER_MONTH = 30.44 * MS_PER_DAY;
const MS_PER_YEAR = 365.25 * MS_PER_DAY;
const MAX_VISIBLE_YEARS = 10;

function toMs(iso?: string | null): number | null {
  if (!iso) return null;
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? null : date.getTime();
}

function effectiveEndMs(exp: DraftExperience, nowMs: number): number | null {
  if (exp.isCurrent) return nowMs;
  return toMs(exp.endDate) ?? nowMs;
}

function rangesOverlap(aStart: number, aEnd: number, bStart: number, bEnd: number): boolean {
  return aStart < bEnd && bStart < aEnd;
}

function snapToMonthMs(ms: number): number {
  const date = new Date(ms);
  return Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), 1);
}

function msToMonthIso(ms: number): string {
  const date = new Date(snapToMonthMs(ms));
  const year = date.getUTCFullYear();
  const month = String(date.getUTCMonth() + 1).padStart(2, "0");
  return `${year}-${month}-01`;
}

function buildRows(experiences: DraftExperience[], nowMs: number): TimelineRow[] {
  return experiences
    .map((exp) => {
      const startMs = toMs(exp.startDate);
      const endMs = effectiveEndMs(exp, nowMs);
      const hasRange = startMs !== null && endMs !== null;
      const snappedStart = hasRange ? snapToMonthMs(startMs) : null;
      return {
        id: exp.id,
        label: exp.title || "Untitled role",
        sublabel: exp.company || "",
        startMs: snappedStart,
        endMs: hasRange ? Math.max(snapToMonthMs(endMs), snappedStart!) : null,
        isCurrent: Boolean(exp.isCurrent),
        include: exp.include
      };
    })
    .sort((a, b) => (b.startMs ?? 0) - (a.startMs ?? 0));
}

function formatAxisYear(ms: number): string {
  return String(new Date(ms).getUTCFullYear());
}

function formatRangeLabel(startMs: number, endMs: number, isCurrent: boolean): string {
  const fmt = (d: Date) =>
    d.toLocaleDateString("en-GB", { month: "short", year: "numeric", timeZone: "UTC" });
  return `${fmt(new Date(startMs))} – ${isCurrent ? "Present" : fmt(new Date(endMs))}`;
}

function applyEdgeDrag(
  edge: "start" | "end",
  pointerMs: number,
  originStartMs: number,
  originEndMs: number,
  nowMs: number
): Pick<DragState, "startMs" | "endMs" | "isCurrent"> {
  const snapped = snapToMonthMs(pointerMs);
  const minDuration = MS_PER_MONTH;
  const presentMonth = snapToMonthMs(nowMs);

  if (edge === "start") {
    return {
      startMs: Math.min(snapped, originEndMs - minDuration),
      endMs: originEndMs,
      isCurrent: originEndMs >= presentMonth
    };
  }

  const endMs = Math.max(snapped, originStartMs + minDuration);
  const isCurrent = endMs >= presentMonth;
  return {
    startMs: originStartMs,
    endMs: isCurrent ? presentMonth : endMs,
    isCurrent
  };
}

export function ExperienceTimeline({
  experiences,
  onChangeExperience,
  onSelectExperience
}: ExperienceTimelineProps) {
  const editable = typeof onChangeExperience === "function";
  const selectable = typeof onSelectExperience === "function";
  const nowMsRef = useRef(Date.now());
  const nowMs = nowMsRef.current;
  const baseRows = buildRows(experiences, nowMs);
  const [drag, setDrag] = useState<DragState | null>(null);
  const trackRefs = useRef(new Map<string, HTMLDivElement>());
  const domainRef = useRef<{ start: number; span: number } | null>(null);
  const dragRef = useRef<DragState | null>(null);
  const scrollRef = useRef<HTMLDivElement | null>(null);
  const onChangeRef = useRef(onChangeExperience);
  const didAutoScroll = useRef(false);
  const suppressClickRef = useRef(false);

  useEffect(() => {
    dragRef.current = drag;
  }, [drag]);

  useEffect(() => {
    onChangeRef.current = onChangeExperience;
  }, [onChangeExperience]);

  useEffect(() => {
    if (!drag) return;

    function clientXToMs(id: string, clientX: number): number | null {
      const track = trackRefs.current.get(id);
      if (!track || !domainRef.current) return null;
      const rect = track.getBoundingClientRect();
      if (rect.width <= 0) return null;
      const ratio = Math.min(1, Math.max(0, (clientX - rect.left) / rect.width));
      return domainRef.current.start + ratio * domainRef.current.span;
    }

    function onMove(event: PointerEvent) {
      const current = dragRef.current;
      if (!current) return;
      const pointerMs = clientXToMs(current.id, event.clientX);
      if (pointerMs === null) return;

      suppressClickRef.current = true;
      const nextRange = applyEdgeDrag(
        current.edge,
        pointerMs,
        current.originStartMs,
        current.originEndMs,
        nowMsRef.current
      );
      const next: DragState = { ...current, ...nextRange };
      dragRef.current = next;
      setDrag(next);
    }

    function onUp() {
      const current = dragRef.current;
      document.body.classList.remove("timeline-dragging");
      if (current) {
        onChangeRef.current?.(current.id, {
          startDate: msToMonthIso(current.startMs),
          endDate: current.isCurrent ? null : msToMonthIso(current.endMs),
          isCurrent: current.isCurrent
        });
      }
      dragRef.current = null;
      setDrag(null);
    }

    window.addEventListener("pointermove", onMove);
    window.addEventListener("pointerup", onUp);
    window.addEventListener("pointercancel", onUp);
    return () => {
      window.removeEventListener("pointermove", onMove);
      window.removeEventListener("pointerup", onUp);
      window.removeEventListener("pointercancel", onUp);
      document.body.classList.remove("timeline-dragging");
    };
  }, [drag]);

  const displayRows = baseRows.map((row) => {
    if (!drag || drag.id !== row.id) return row;
    return {
      ...row,
      startMs: drag.startMs,
      endMs: drag.endMs,
      isCurrent: drag.isCurrent
    };
  });

  const datedRows = displayRows.filter(
    (row): row is TimelineRow & { startMs: number; endMs: number } =>
      row.startMs !== null && row.endMs !== null
  );

  if (baseRows.length === 0) {
    return (
      <div className="timeline-empty muted">
        No experience roles on this application yet.
      </div>
    );
  }

  const domainSource = datedRows;
  const minStart =
    domainSource.length > 0 ? Math.min(...domainSource.map((r) => r.startMs)) : nowMs - MS_PER_YEAR;
  const maxEnd =
    domainSource.length > 0
      ? Math.max(...domainSource.map((r) => r.endMs), nowMs)
      : nowMs;
  const rawSpan = Math.max(maxEnd - minStart, MS_PER_YEAR);
  const pad = Math.max(rawSpan * 0.08, MS_PER_YEAR * 0.6);

  let domainStart = minStart - pad;
  let domainSpan = maxEnd + pad - domainStart;

  if (drag && domainRef.current) {
    domainStart = domainRef.current.start;
    domainSpan = domainRef.current.span;
  } else if (!drag) {
    domainRef.current = { start: domainStart, span: domainSpan };
  }

  const domainEnd = domainStart + domainSpan;
  const startYear = new Date(domainStart).getUTCFullYear();
  const endYear = new Date(domainEnd).getUTCFullYear();
  const yearMarks: number[] = [];
  for (let year = startYear; year <= endYear; year++) {
    yearMarks.push(Date.UTC(year, 0, 1));
  }

  const includedDated = datedRows.filter((row) => row.include);
  const overlapIds = new Set(
    includedDated
      .filter((row) =>
        includedDated.some(
          (other) =>
            other.id !== row.id &&
            rangesOverlap(row.startMs, row.endMs, other.startMs, other.endMs)
        )
      )
      .map((r) => r.id)
  );

  const includedCount = displayRows.filter((r) => r.include).length;
  const contentYears = domainSpan / MS_PER_YEAR;
  const scrollWidthPercent = Math.max(100, (contentYears / MAX_VISIBLE_YEARS) * 100);
  const isScrollable = scrollWidthPercent > 100.5;

  useEffect(() => {
    if (!isScrollable || didAutoScroll.current) return;
    const node = scrollRef.current;
    if (!node) return;
    node.scrollLeft = node.scrollWidth - node.clientWidth;
    didAutoScroll.current = true;
  }, [isScrollable, scrollWidthPercent, displayRows.length]);

  function beginDrag(
    event: ReactPointerEvent<HTMLButtonElement>,
    row: TimelineRow & { startMs: number; endMs: number },
    edge: "start" | "end"
  ) {
    if (!editable) return;
    event.preventDefault();
    event.stopPropagation();

    domainRef.current = { start: domainStart, span: domainSpan };
    const next: DragState = {
      id: row.id,
      edge,
      originStartMs: row.startMs,
      originEndMs: row.endMs,
      startMs: row.startMs,
      endMs: row.endMs,
      isCurrent: row.isCurrent
    };
    dragRef.current = next;
    setDrag(next);
    document.body.classList.add("timeline-dragging");
  }

  return (
    <div className={`timeline${editable ? " timeline-editable" : ""}`}>
      <div className="timeline-header">
        <div>
          <h4 className="timeline-title">Timeline</h4>
          <p className="muted timeline-copy">
            All roles · {includedCount} included on CV
            {overlapIds.size > 0 ? ` · ${overlapIds.size} overlapping` : ""}
            {isScrollable ? " · scroll to see full career" : ""}
            {editable ? " · toggle include · drag bar ends · click a bar to edit" : ""}
          </p>
        </div>
      </div>

      <div className="timeline-body">
        <div className="timeline-label-col">
          <div className="timeline-axis-spacer" aria-hidden="true" />
          {displayRows.map((row) => (
            <div
              key={row.id}
              className={`timeline-label${row.include ? "" : " is-excluded"}`}
            >
              {editable ? (
                <label className="timeline-include">
                  <input
                    type="checkbox"
                    checked={row.include}
                    onChange={(e) =>
                      onChangeExperience?.(row.id, { include: e.target.checked })
                    }
                  />
                  <span className="timeline-label-text">
                    <span className="timeline-label-title">{row.label}</span>
                    {row.sublabel && (
                      <span className="timeline-label-sub">{row.sublabel}</span>
                    )}
                  </span>
                </label>
              ) : (
                <>
                  <span className="timeline-label-title">{row.label}</span>
                  {row.sublabel && <span className="timeline-label-sub">{row.sublabel}</span>}
                </>
              )}
            </div>
          ))}
        </div>

        <div className="timeline-scroll" ref={scrollRef}>
          <div
            className="timeline-scroll-inner"
            style={{ width: `${scrollWidthPercent}%`, minWidth: "100%" }}
          >
            <div className="timeline-axis-track" aria-hidden="true">
              {yearMarks.map((mark) => {
                const left = ((mark - domainStart) / domainSpan) * 100;
                if (left < -2 || left > 102) return null;
                return (
                  <span key={mark} className="timeline-year" style={{ left: `${left}%` }}>
                    {formatAxisYear(mark)}
                  </span>
                );
              })}
            </div>

            <ul className="timeline-rows">
              {displayRows.map((row) => {
                const hasRange = row.startMs !== null && row.endMs !== null;
                const left = hasRange ? ((row.startMs! - domainStart) / domainSpan) * 100 : 0;
                const width = hasRange
                  ? ((row.endMs! - row.startMs!) / domainSpan) * 100
                  : 0;
                const active = drag?.id === row.id;

                return (
                  <li
                    key={row.id}
                    className={`timeline-row${row.include ? "" : " is-excluded"}${active ? " is-dragging" : ""}`}
                  >
                    <div
                      className="timeline-track"
                      ref={(node) => {
                        if (node) trackRefs.current.set(row.id, node);
                        else trackRefs.current.delete(row.id);
                      }}
                    >
                      {hasRange ? (
                        <div
                          role={selectable ? "button" : undefined}
                          tabIndex={selectable ? 0 : undefined}
                          className={`timeline-bar${active ? " is-active" : ""}${selectable ? " is-selectable" : ""}`}
                          style={{
                            left: `${left}%`,
                            width: `${Math.max(width, 0.8)}%`
                          }}
                          title={`${row.label}${row.sublabel ? ` · ${row.sublabel}` : ""} · ${formatRangeLabel(row.startMs!, row.endMs!, row.isCurrent)}${row.include ? "" : " · excluded"}`}
                          onClick={() => {
                            if (!selectable) return;
                            if (suppressClickRef.current) {
                              suppressClickRef.current = false;
                              return;
                            }
                            onSelectExperience?.(row.id);
                          }}
                          onKeyDown={(event) => {
                            if (!selectable) return;
                            if (event.key === "Enter" || event.key === " ") {
                              event.preventDefault();
                              onSelectExperience?.(row.id);
                            }
                          }}
                        >
                          {editable && (
                            <button
                              type="button"
                              className="timeline-handle timeline-handle-start"
                              aria-label={`Adjust start date for ${row.label}`}
                              onPointerDown={(event) =>
                                beginDrag(
                                  event,
                                  row as TimelineRow & { startMs: number; endMs: number },
                                  "start"
                                )
                              }
                              onClick={(event) => event.stopPropagation()}
                            />
                          )}
                          <span className="timeline-bar-text">
                            {formatRangeLabel(row.startMs!, row.endMs!, row.isCurrent)}
                          </span>
                          {editable && (
                            <button
                              type="button"
                              className="timeline-handle timeline-handle-end"
                              aria-label={`Adjust end date for ${row.label}`}
                              onPointerDown={(event) =>
                                beginDrag(
                                  event,
                                  row as TimelineRow & { startMs: number; endMs: number },
                                  "end"
                                )
                              }
                              onClick={(event) => event.stopPropagation()}
                            />
                          )}
                        </div>
                      ) : (
                        <span className="timeline-no-dates muted">No dates set</span>
                      )}
                    </div>
                  </li>
                );
              })}
            </ul>
          </div>
        </div>
      </div>
    </div>
  );
}
