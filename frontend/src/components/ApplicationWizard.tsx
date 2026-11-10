import type { FormEvent } from "react";
import { Link } from "react-router-dom";
import type { ApplicationDetail, DraftExperience } from "../api";
import { ExperienceTimeline, type ExperienceDatePatch } from "./ExperienceTimeline";
import { formatDateRange } from "../lib/dates";

export type OpenWizardStep = "ad" | "requirements" | "fit" | "roles";

export type WizardStepId = OpenWizardStep | "review";

export type WizardBusy = "create" | "save" | "score" | "tailor" | null;

export const WIZARD_STEPS: { id: WizardStepId; label: string }[] = [
  { id: "ad", label: "Job ad" },
  { id: "requirements", label: "Requirements" },
  { id: "fit", label: "Your angle" },
  { id: "roles", label: "Roles" },
  { id: "review", label: "Review" }
];

export function wizardStepIndex(step?: string | null) {
  const index = WIZARD_STEPS.findIndex((item) => item.id === step);
  return index < 0 ? 0 : index;
}

const RELEVANCE_CUTOFF = 70;

type ApplicationWizardProps = {
  step: OpenWizardStep;
  jobAd: string;
  onJobAdChange: (value: string) => void;
  credits: number | null;
  draft: ApplicationDetail | null;
  fitNotes: string;
  onFitNotesChange: (value: string) => void;
  selectedIds: string[];
  onToggleRole: (id: string, included: boolean) => void;
  onChangeRole: (id: string, patch: ExperienceDatePatch) => void;
  busy: WizardBusy;
  isNew: boolean;
  descriptionsReady: boolean;
  isStepEnabled: (step: WizardStepId) => boolean;
  onOpen: (step: WizardStepId) => void;
  onContinue: () => void;
  onBack: () => void;
};

export function isOpenWizardStep(step?: string | null): step is OpenWizardStep {
  return step === "ad" || step === "requirements" || step === "fit" || step === "roles";
}

export function WizardStepper({
  current,
  busy,
  isStepEnabled,
  onOpen
}: {
  current: WizardStepId;
  busy: boolean;
  isStepEnabled: (step: WizardStepId) => boolean;
  onOpen: (step: WizardStepId) => void;
}) {
  const stepIndex = wizardStepIndex(current);
  return (
    <ol className="wizard-steps">
      {WIZARD_STEPS.map((item, index) => {
        const state = index < stepIndex ? "is-done" : index === stepIndex ? "is-current" : "";
        const enabled = isStepEnabled(item.id) && !busy && item.id !== current;
        return (
          <li key={item.id}>
            <button
              type="button"
              className={`wizard-step ${state}`.trim()}
              aria-current={index === stepIndex ? "step" : undefined}
              disabled={!enabled && item.id !== current}
              onClick={() => {
                if (enabled) onOpen(item.id);
              }}
            >
              <span className="wizard-step-bar" />
              <span>{item.label}</span>
            </button>
          </li>
        );
      })}
    </ol>
  );
}

export function ApplicationWizard({
  step,
  jobAd,
  onJobAdChange,
  credits,
  draft,
  fitNotes,
  onFitNotesChange,
  selectedIds,
  onToggleRole,
  onChangeRole,
  busy,
  isNew,
  descriptionsReady,
  isStepEnabled,
  onOpen,
  onContinue,
  onBack
}: ApplicationWizardProps) {
  const roles = [...(draft?.experiences ?? [])].sort(byStartDate);
  const selectedCount = roles.filter((role) => selectedIds.includes(role.id)).length;
  const continueDisabled =
    busy !== null ||
    (step === "ad" && (!jobAd.trim() || (isNew && (credits === null || credits < 1))));

  function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (!continueDisabled) onContinue();
  }

  return (
    <section className="panel app-page-panel">
      <WizardStepper current={step} busy={busy !== null} isStepEnabled={isStepEnabled} onOpen={onOpen} />

      <form className={`form${busy ? " form-busy" : ""}`} onSubmit={onSubmit} aria-busy={busy !== null}>
        {step === "ad" && (
          <>
            <div className="panel-header">
              <h2>{isNew ? "New application" : "Job ad"}</h2>
            </div>
            <p className="wizard-lead muted">
              {isNew
                ? "Paste the job ad. Next we'll pull out the must-haves and nice-to-haves."
                : "This is the ad used for the requirements. Change it to read them again."}
            </p>
            <label>
              Job ad
              <textarea
                value={jobAd}
                onChange={(event) => onJobAdChange(event.target.value)}
                rows={16}
                required
                disabled={busy !== null || credits === 0}
                placeholder="Paste the full job advertisement here…"
              />
            </label>
            {isNew && credits === 0 && (
              <p className="muted">You need a credit before you can start an application.</p>
            )}
            {isNew && credits !== null && credits > 0 && (
              <p className="muted">Uses 1 credit. You have {credits} left.</p>
            )}
            {busy === "create" && (
              <Busy title="Reading the job ad…" detail="Pulling out must-haves and nice-to-haves." />
            )}
          </>
        )}

        {step === "requirements" && draft && (
          <>
            <div className="panel-header">
              <h2>What this role is looking for</h2>
            </div>
            <p className="wizard-lead">
              <strong>{draft.roleTitle || "Untitled role"}</strong>
              <span className="muted"> · {draft.company || "Unknown company"}</span>
            </p>
            <p className="wizard-lead muted">
              Taken from the ad. We'll use these when scoring your roles and writing the CV.
            </p>
            <RequirementColumns mustHave={draft.mustHave ?? []} niceToHave={draft.niceToHave ?? []} />
          </>
        )}

        {step === "fit" && (
          <>
            <div className="panel-header">
              <h2>Anything we should emphasise?</h2>
            </div>
            <p className="wizard-lead muted">
              Optional. Mention work that's relevant to this role, or why it's a good fit. We'll use it
              when scoring roles and writing descriptions. Leave it blank to continue.
            </p>
            <label>
              Your notes
              <textarea
                value={fitNotes}
                onChange={(event) => onFitNotesChange(event.target.value)}
                rows={8}
                disabled={busy !== null}
                placeholder="e.g. I led the payments migration at Northwind, and this role matches the platform work I want to keep doing."
              />
            </label>
            {busy === "score" && (
              <Busy title="Scoring your roles…" detail="Comparing each role with the requirements." />
            )}
          </>
        )}

        {step === "roles" && draft && (
          <>
            <div className="panel-header">
              <h2>Roles to include</h2>
            </div>
            <p className="wizard-lead muted">
              {descriptionsReady
                ? "This selection already has tailored titles and descriptions. Continue to review them."
                : draft.descriptionsWritten
                  ? "Roles already written keep their titles and descriptions. Continue to write only the roles you just added."
                  : `Scores of ${RELEVANCE_CUTOFF} and above start selected. Uncheck anything that shouldn't be on this CV. Titles and descriptions are written in the next step.`}
            </p>
            {roles.length === 0 ? (
              <p className="muted">Your profile has no roles. Continue to write the summary from your education.</p>
            ) : (
              <>
                <p className="muted wizard-count">
                  {selectedCount} of {roles.length} selected
                  {selectedCount === 0 ? ". The CV will rely on education and the summary." : "."}
                </p>
                <ExperienceTimeline
                  experiences={roles.map((role) => ({
                    ...role,
                    include: selectedIds.includes(role.id)
                  }))}
                  onChangeExperience={
                    busy
                      ? undefined
                      : (id, patch) => {
                          if (typeof patch.include === "boolean") onToggleRole(id, patch.include);
                          const dates: ExperienceDatePatch = {};
                          if ("startDate" in patch) dates.startDate = patch.startDate;
                          if ("endDate" in patch) dates.endDate = patch.endDate;
                          if ("isCurrent" in patch) dates.isCurrent = patch.isCurrent;
                          if (Object.keys(dates).length > 0) onChangeRole(id, dates);
                        }
                  }
                />
                <ul className="role-picker">
                  {roles.map((role) => {
                    const selected = selectedIds.includes(role.id);
                    return (
                      <li key={role.id}>
                        <label className={`role-card${selected ? " is-selected" : ""}`}>
                          <input
                            type="checkbox"
                            checked={selected}
                            disabled={busy !== null}
                            onChange={(event) => onToggleRole(role.id, event.target.checked)}
                          />
                          <span className="role-card-body">
                            <span className="list-row">
                              <strong>{role.title || "Untitled role"}</strong>
                              <span className={`score-pill${role.relevanceScore >= RELEVANCE_CUTOFF ? " is-high" : ""}`}>
                                {Math.round(role.relevanceScore)}
                              </span>
                            </span>
                            <span className="muted role-meta">
                              {role.company || "Unknown company"}
                              {" · "}
                              {formatDateRange(role.startDate, role.endDate, role.isCurrent)}
                            </span>
                            <span className="role-snippet">{roleSnippet(role.description)}</span>
                          </span>
                        </label>
                      </li>
                    );
                  })}
                </ul>
              </>
            )}
            {busy === "tailor" && (
              <Busy
                title="Writing descriptions…"
                detail={
                  draft.descriptionsWritten
                    ? "Writing titles and bullets for the roles you added."
                    : "Tailoring titles and bullets for the roles you picked."
                }
              />
            )}
          </>
        )}

        <div className="form-actions">
          {step === "ad" && isNew ? (
            <Link to="/" className={`btn btn-ghost${busy ? " is-disabled" : ""}`} aria-disabled={busy !== null}>
              Cancel
            </Link>
          ) : (
            <button type="button" className="btn btn-ghost" disabled={busy !== null || step === "ad"} onClick={onBack}>
              Back
            </button>
          )}
          <button type="submit" className="btn btn-primary" disabled={continueDisabled}>
            {busy === "create" || busy === "score" || busy === "tailor" ? (
              <>
                <span className="spinner spinner-on-dark" aria-hidden="true" />
                {busy === "create" ? "Reading…" : busy === "score" ? "Scoring…" : "Writing…"}
              </>
            ) : busy === "save" ? (
              "Saving…"
            ) : (
              "Continue"
            )}
          </button>
        </div>
      </form>
    </section>
  );
}

function RequirementColumns({ mustHave, niceToHave }: { mustHave: string[]; niceToHave: string[] }) {
  return (
    <div className="req-grid">
      <div>
        <h3 className="wizard-req-heading">Must have</h3>
        <RequirementList items={mustHave} mark="req-mark-must" empty="None inferred from this ad." />
      </div>
      <div>
        <h3 className="wizard-req-heading">Nice to have</h3>
        <RequirementList items={niceToHave} mark="req-mark-nice" empty="None inferred from this ad." />
      </div>
    </div>
  );
}

function RequirementList({ items, mark, empty }: { items: string[]; mark: string; empty: string }) {
  if (items.length === 0) return <p className="muted">{empty}</p>;
  return (
    <ul className="req-list">
      {items.map((item) => (
        <li key={item}>
          <span className={`req-mark ${mark}`} aria-hidden="true" />
          <span>{item}</span>
        </li>
      ))}
    </ul>
  );
}

function Busy({ title, detail }: { title: string; detail: string }) {
  return (
    <div className="busy-banner" role="status" aria-live="polite">
      <span className="spinner" aria-hidden="true" />
      <div>
        <strong>{title}</strong>
        <p className="muted">{detail}</p>
      </div>
    </div>
  );
}

function byStartDate(a: DraftExperience, b: DraftExperience) {
  return startMs(b.startDate) - startMs(a.startDate);
}

function startMs(value?: string | null) {
  if (!value) return 0;
  const time = Date.parse(value);
  return Number.isNaN(time) ? 0 : time;
}

function roleSnippet(description?: string | null) {
  if (!description?.trim()) return "No description on the profile yet.";
  const flat = description
    .split("\n")
    .map((line) => line.replace(/^[-•*]\s*/, "").trim())
    .filter(Boolean)
    .join(" ");
  if (flat.length <= 180) return flat;
  return `${flat.slice(0, 177).trimEnd()}…`;
}
