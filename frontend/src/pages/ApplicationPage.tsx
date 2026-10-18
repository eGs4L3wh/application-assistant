import { useEffect, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { api, type ApplicationDetail, type DraftExperience, type RequirementCoverage } from "../api";
import {
  ApplicationWizard,
  WIZARD_STEPS,
  WizardStepper,
  isOpenWizardStep,
  wizardStepIndex,
  type WizardBusy,
  type WizardStepId
} from "../components/ApplicationWizard";
import { formatDateRange } from "../lib/dates";
import { downloadBlob } from "../lib/download";
import { useAuth } from "../auth/AuthContext";

function RequirementColumn({
  title,
  mark,
  items,
  coverage
}: {
  title: string;
  mark: string;
  items: string[];
  coverage: RequirementCoverage[] | null;
}) {
  return (
    <div>
      <h3 className="wizard-req-heading">{title}</h3>
      {items.length === 0 ? (
        <p className="muted">None inferred from this ad.</p>
      ) : (
        <ul className="req-list">
          {items.map((item) => {
            const result = coverage?.find(
              (entry) => entry.requirement.localeCompare(item, undefined, { sensitivity: "accent" }) === 0
            );
            return (
              <li key={item}>
                <span className={`req-mark ${mark}`} aria-hidden="true" />
                <span>
                  <span className="coverage-heading">
                    <span>{item}</span>
                    {result && (
                      <span className={`coverage-badge coverage-${result.coverage}`}>{result.coverage}</span>
                    )}
                  </span>
                  {result && <p>{result.evidence}</p>}
                </span>
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}

export default function ApplicationPage() {
  const { id } = useParams<{ id: string }>();
  const isNew = !id || id === "new";
  const navigate = useNavigate();
  const { user, logout } = useAuth();

  const [jobAd, setJobAd] = useState("");
  const [draft, setDraft] = useState<ApplicationDetail | null>(null);
  const [loading, setLoading] = useState(() => !isNew);
  const [wizardBusy, setWizardBusy] = useState<WizardBusy>(null);
  const [fitNotes, setFitNotes] = useState("");
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [savingDraft, setSavingDraft] = useState(false);
  const [finalizing, setFinalizing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [aiEditExperienceId, setAiEditExperienceId] = useState<string | null>(null);
  const [aiEditPrompt, setAiEditPrompt] = useState("");
  const [aiEditingId, setAiEditingId] = useState<string | null>(null);
  const [credits, setCredits] = useState<number | null>(null);
  const [coverage, setCoverage] = useState<RequirementCoverage[] | null>(null);
  const [checkingCoverage, setCheckingCoverage] = useState(false);

  useEffect(() => {
    if (!isNew) return;
    let cancelled = false;
    void api.getProfile()
      .then((profile) => {
        if (!cancelled) setCredits(profile.credits);
      })
      .catch((err) => {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : "Could not load credits");
        }
      });
    return () => {
      cancelled = true;
    };
  }, [isNew]);

  useEffect(() => {
    if (isNew) {
      setDraft(null);
      setJobAd("");
      setWizardBusy(null);
      setLoading(false);
      return;
    }

    let cancelled = false;
    setLoading(true);
    setWizardBusy(null);
    setError(null);

    void (async () => {
      try {
        const detail = await api.getApplication(id!);
        if (cancelled) return;
        setDraft(detail);
        setJobAd(detail.jobAd ?? "");
      } catch (err) {
        if (cancelled) return;
        setError(err instanceof Error ? err.message : "Could not open application");
        setDraft(null);
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [id, isNew]);

  useEffect(() => {
    setFitNotes(draft?.fitNotes ?? "");
  }, [draft?.id, draft?.fitNotes]);

  useEffect(() => {
    if (!draft || draft.wizardStep !== "roles") return;
    setSelectedIds(draft.experiences.filter((exp) => exp.include).map((exp) => exp.id));
  }, [draft?.id, draft?.updatedAt, draft?.wizardStep]);

  async function onCreatePreview() {
    if (wizardBusy || !jobAd.trim() || credits === null || credits < 1) return;
    setWizardBusy("create");
    setError(null);
    try {
      const detail = await api.previewApplication(jobAd);
      navigate(`/applications/${detail.id}`, { replace: true });
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not read the job ad");
      setWizardBusy(null);
    }
  }

  function currentSelection() {
    if (!draft) return [];
    if (draft.wizardStep === "roles") return selectedIds;
    return draft.experiences.filter((exp) => exp.include).map((exp) => exp.id);
  }

  function forwardLimitIndex() {
    if (isNew || !draft) return 0;
    let limit = wizardStepIndex(draft.wizardReached || draft.wizardStep || "requirements");
    if (jobAd.trim() !== (draft.jobAd ?? "").trim()) return wizardStepIndex("ad");
    if (draft.scoredFitNotes != null && fitNotes.trim() !== draft.scoredFitNotes.trim()) {
      limit = Math.min(limit, wizardStepIndex("fit"));
    }
    if (
      draft.descriptionsWritten &&
      !sameIds(currentSelection(), draft.tailoredExperienceIds ?? [])
    ) {
      limit = Math.min(limit, wizardStepIndex("roles"));
    }
    return limit;
  }

  function isWizardStepEnabled(target: WizardStepId) {
    if (wizardBusy) return false;
    if (isNew) return target === "ad";
    return wizardStepIndex(target) <= forwardLimitIndex() + 1;
  }

  async function runWizard(busy: Exclude<WizardBusy, null>, action: () => Promise<void>) {
    if (wizardBusy) return;
    setWizardBusy(busy);
    setError(null);
    try {
      await action();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not continue");
    } finally {
      setWizardBusy(null);
    }
  }

  async function saveWizardPosition(target: WizardStepId) {
    if (!draft) return;
    if (!draft.wizardStep || draft.wizardStep === "review") {
      await persistDraft();
    }
    const saved = await api.updateApplicationDraft(draft.id, {
      wizardStep: target,
      fitNotes,
      ...(draft.wizardStep === "roles"
        ? {
            experiences: draft.experiences.map((exp) => ({
              ...exp,
              include: selectedIds.includes(exp.id),
              isCurrent: exp.isCurrent ?? false,
              engagementType: exp.engagementType || "Permanent",
              endDate: exp.isCurrent ? null : exp.endDate
            }))
          }
        : {})
    });
    setDraft(saved);
  }

  async function onWizardOpen(target: WizardStepId) {
    if (!draft || !isWizardStepEnabled(target)) return;
    const current = wizardStepIndex(draft.wizardStep);
    if (wizardStepIndex(target) === current) return;
    if (wizardStepIndex(target) <= forwardLimitIndex()) {
      await runWizard("save", () => saveWizardPosition(target));
      return;
    }
    await unlockNext();
  }

  async function unlockNext() {
    if (!draft) return;
    const next = WIZARD_STEPS[forwardLimitIndex() + 1]?.id;
    if (!next) return;
    if (next === "requirements") {
      await runWizard("create", async () => {
        const saved = await api.rereadJobAd(draft.id, jobAd.trim());
        setJobAd(saved.jobAd ?? jobAd.trim());
        setDraft(saved);
      });
      return;
    }
    if (next === "fit") {
      await runWizard("save", () => saveWizardPosition("fit"));
      return;
    }
    if (next === "roles") {
      await runWizard("score", async () => {
        setDraft(await api.scoreRelevance(draft.id, fitNotes));
      });
      return;
    }
    await runWizard("tailor", async () => {
      if (draft.wizardStep === "roles") {
        await api.updateApplicationDraft(draft.id, {
          fitNotes,
          experiences: draft.experiences.map((exp) => ({
            ...exp,
            include: selectedIds.includes(exp.id),
            isCurrent: exp.isCurrent ?? false,
            engagementType: exp.engagementType || "Permanent",
            endDate: exp.isCurrent ? null : exp.endDate
          }))
        });
      }
      setDraft(await api.tailorApplication(draft.id, { experienceIds: currentSelection(), fitNotes }));
    });
  }

  async function onWizardContinue() {
    if (isNew) {
      await onCreatePreview();
      return;
    }
    if (!draft || wizardBusy) return;
    const current = wizardStepIndex(draft.wizardStep);
    const limit = forwardLimitIndex();
    if (current < limit) {
      const next = WIZARD_STEPS[current + 1]?.id;
      if (!next) return;
      await runWizard("save", () => saveWizardPosition(next));
      return;
    }
    await unlockNext();
  }

  async function onWizardBack() {
    if (!draft || wizardBusy) return;
    const current = wizardStepIndex(draft.wizardStep);
    if (current <= 0) return;
    const previous = WIZARD_STEPS[current - 1]?.id;
    if (!previous) return;
    await runWizard("save", () => saveWizardPosition(previous));
  }

  function sameIds(left: string[], right: string[]) {
    if (left.length !== right.length) return false;
    const ids = new Set(left);
    return right.every((id) => ids.has(id));
  }

  function onToggleRole(id: string, included: boolean) {
    setCoverage(null);
    setSelectedIds((current) =>
      included ? (current.includes(id) ? current : [...current, id]) : current.filter((item) => item !== id)
    );
  }

  async function onCheckCoverage() {
    if (!draft || checkingCoverage) return;
    setCheckingCoverage(true);
    setError(null);
    try {
      const result = await api.assessCoverage(draft.id, {
        experiences: draft.experiences
          .filter((exp) => exp.include)
          .map((exp) => ({
            company: exp.company,
            title: exp.title,
            description: exp.description,
            skills: exp.skills ?? []
          })),
        education: draft.education
          .filter((edu) => edu.include)
          .map((edu) => ({
            institution: edu.institution,
            degree: edu.degree,
            fieldOfStudy: edu.fieldOfStudy,
            description: edu.description
          }))
      });
      setCoverage(result.items);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not check requirement coverage");
    } finally {
      setCheckingCoverage(false);
    }
  }

  function updateDraftExperience(experienceId: string, patch: Partial<DraftExperience>) {
    setCoverage(null);
    setDraft((current) => {
      if (!current) return current;
      return {
        ...current,
        experiences: current.experiences.map((exp) =>
          exp.id === experienceId ? { ...exp, ...patch } : exp
        )
      };
    });
  }

  function openAiEdit(experienceId: string) {
    setAiEditExperienceId(experienceId);
    setAiEditPrompt("");
  }

  function closeAiEdit() {
    setAiEditExperienceId(null);
    setAiEditPrompt("");
  }

  async function onAiEditExperience(experienceId: string) {
    if (!draft || !aiEditPrompt.trim()) return;
    setAiEditingId(experienceId);
    setError(null);
    try {
      const updated = await api.refineExperience(draft.id, experienceId, aiEditPrompt.trim());
      updateDraftExperience(experienceId, {
        description: updated.description,
        title: updated.title,
        company: updated.company
      });
      closeAiEdit();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not refine description");
    } finally {
      setAiEditingId(null);
    }
  }

  async function persistDraft(): Promise<ApplicationDetail | null> {
    if (!draft) return null;
    return api.updateApplicationDraft(draft.id, {
      company: draft.company,
      roleTitle: draft.roleTitle,
      notes: draft.notes ?? "",
      summary: draft.summary ?? "",
      skills: draft.skills,
      experiences: draft.experiences.map((exp) => ({
        ...exp,
        isCurrent: exp.isCurrent ?? false,
        engagementType: exp.engagementType || "Permanent",
        endDate: exp.isCurrent ? null : exp.endDate
      })),
      education: draft.education
    });
  }

  async function onSave() {
    if (!draft) {
      navigate("/");
      return;
    }
    setSavingDraft(true);
    setError(null);
    try {
      const saved = await persistDraft();
      if (saved) setDraft(saved);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not save draft");
    } finally {
      setSavingDraft(false);
    }
  }

  async function onSaveAndGenerate() {
    if (!draft) return;
    setSavingDraft(true);
    setFinalizing(true);
    setError(null);
    try {
      await persistDraft();
      const { blob, fileName } = await api.finalizeApplication(draft.id);
      downloadBlob(blob, fileName);
      setDraft((current) =>
        current
          ? { ...current, status: "Generated", cvFileName: fileName, hasGeneratedCv: true }
          : current
      );
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not generate CV");
    } finally {
      setSavingDraft(false);
      setFinalizing(false);
    }
  }

  async function onDownloadCv() {
    if (!draft) return;
    setError(null);
    try {
      const { blob, fileName } = await api.downloadApplicationCv(draft.id);
      downloadBlob(blob, draft.cvFileName?.trim() || fileName);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not download CV");
    }
  }

  async function onDelete() {
    if (!draft) return;
    if (!window.confirm("Delete this application? The saved draft and generated CV will be removed.")) {
      return;
    }
    setError(null);
    try {
      await api.deleteApplication(draft.id);
      navigate("/");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not delete application");
    }
  }

  const pinReviewActions =
    Boolean(draft) && !isNew && !loading && !isOpenWizardStep(draft?.wizardStep);

  return (
    <div className={`app-page${pinReviewActions ? " app-page-with-actions" : ""}`}>
      <header className="dash-header">
        <div>
          <p className="brand">Application Assistant</p>
          <p className="muted">Signed in as {user?.name ?? user?.email}</p>
        </div>
        <div className="app-page-header-actions">
          <Link to="/" className="btn btn-ghost btn-small">
            Dashboard
          </Link>
          <Link to="/profile" className="btn btn-ghost btn-small">
            Profile
          </Link>
          <button type="button" className="btn btn-ghost btn-small" onClick={() => void logout()}>
            Sign out
          </button>
        </div>
      </header>

      {error && <div className="banner error">{error}</div>}

      {loading ? (
        <p className="muted">Loading application…</p>
      ) : isNew || (draft && isOpenWizardStep(draft.wizardStep)) ? (
        <ApplicationWizard
          step={isNew || !draft || !isOpenWizardStep(draft.wizardStep) ? "ad" : draft.wizardStep}
          jobAd={jobAd}
          onJobAdChange={setJobAd}
          credits={credits}
          draft={draft}
          fitNotes={fitNotes}
          onFitNotesChange={setFitNotes}
          selectedIds={selectedIds}
          onToggleRole={onToggleRole}
          onChangeRole={(id, patch) => updateDraftExperience(id, patch)}
          busy={wizardBusy}
          isNew={isNew}
          descriptionsReady={
            Boolean(draft?.descriptionsWritten) &&
            sameIds(selectedIds, draft?.tailoredExperienceIds ?? [])
          }
          isStepEnabled={isWizardStepEnabled}
          onOpen={(target) => void onWizardOpen(target)}
          onContinue={() => void onWizardContinue()}
          onBack={() => void onWizardBack()}
        />
      ) : !draft ? (
        <section className="panel app-page-panel">
          <p className="muted">Application not found.</p>
          <div className="form-actions">
            <Link to="/" className="btn btn-ghost">
              Back to dashboard
            </Link>
          </div>
        </section>
      ) : (
        <>
          <div className="app-page-main">
            <WizardStepper
              current="review"
              busy={wizardBusy !== null || savingDraft || finalizing}
              isStepEnabled={isWizardStepEnabled}
              onOpen={(target) => void onWizardOpen(target)}
            />
            <header className="app-page-title">
              <div>
                <h1>{draft.roleTitle || "Untitled role"}</h1>
                <p className="muted">
                  {draft.company || "Unknown company"}
                  {draft.status ? ` · ${draft.status}` : ""}
                </p>
              </div>
            </header>

            <section className="panel app-page-panel">
              <div className="panel-header">
                <h2>Requirements</h2>
                <button
                  type="button"
                  className="btn btn-ghost btn-small"
                  disabled={
                    checkingCoverage ||
                    ((draft.mustHave ?? []).length === 0 && (draft.niceToHave ?? []).length === 0)
                  }
                  onClick={() => void onCheckCoverage()}
                >
                  {checkingCoverage ? "Checking…" : "Check coverage"}
                </button>
              </div>
              <div className="req-grid">
                <RequirementColumn
                  title="Must have"
                  mark="req-mark-must"
                  items={draft.mustHave ?? []}
                  coverage={coverage?.filter((item) => item.kind !== "nice") ?? null}
                />
                <RequirementColumn
                  title="Nice to have"
                  mark="req-mark-nice"
                  items={draft.niceToHave ?? []}
                  coverage={coverage?.filter((item) => item.kind === "nice") ?? null}
                />
              </div>
            </section>

            <section className="panel app-page-panel form">
              <h2>Experience</h2>
              <p className="muted">
                Titles and descriptions written for the roles you included. Edit them before generating the CV.
              </p>
              <ul className="draft-list">
                  {draft.experiences.filter((exp) => exp.include).length === 0 ? (
                    <li>
                      <p className="muted">No roles included. Go back and choose at least one.</p>
                    </li>
                  ) : null}
                  {draft.experiences
                    .filter((exp) => exp.include)
                    .map((exp) => (
                    <li key={exp.id}>
                      <p className="muted wizard-lead">
                        {formatDateRange(exp.startDate, exp.endDate, exp.isCurrent)}
                      </p>
                      <div className="form-row">
                        <label>
                          Title
                          <input
                            value={exp.title}
                            onChange={(e) =>
                              updateDraftExperience(exp.id, { title: e.target.value })
                            }
                          />
                        </label>
                        <label>
                          Company
                          <input
                            value={exp.company}
                            onChange={(e) =>
                              updateDraftExperience(exp.id, { company: e.target.value })
                            }
                          />
                        </label>
                      </div>
                      <div className="field-with-action">
                        <div className="list-row">
                          <span className="field-label">Description</span>
                          <button
                            type="button"
                            className="btn btn-ghost btn-small"
                            onClick={() =>
                              aiEditExperienceId === exp.id ? closeAiEdit() : openAiEdit(exp.id)
                            }
                          >
                            {aiEditExperienceId === exp.id ? "Cancel" : "AI edit"}
                          </button>
                        </div>
                        <textarea
                          value={exp.description ?? ""}
                          onChange={(e) =>
                            updateDraftExperience(exp.id, {
                              description: e.target.value || null
                            })
                          }
                          rows={8}
                          placeholder={"- Built and managed a team of 5 engineers\n- Implemented payment backend integration using Kafka and event sourcing"}
                        />
                        {aiEditExperienceId === exp.id && (
                          <div className="ai-edit-row">
                            <input
                              value={aiEditPrompt}
                              onChange={(e) => setAiEditPrompt(e.target.value)}
                              placeholder="e.g. emphasise leadership and cloud delivery"
                              disabled={aiEditingId === exp.id}
                              onKeyDown={(e) => {
                                if (e.key === "Enter") {
                                  e.preventDefault();
                                  void onAiEditExperience(exp.id);
                                }
                              }}
                            />
                            <button
                              type="button"
                              className="btn btn-primary btn-small"
                              disabled={aiEditingId === exp.id || !aiEditPrompt.trim()}
                              onClick={() => void onAiEditExperience(exp.id)}
                            >
                              {aiEditingId === exp.id ? "Editing…" : "Apply"}
                            </button>
                          </div>
                        )}
                      </div>
                    </li>
                  ))}
                </ul>
            </section>
          </div>

          <div className="app-page-actions">
            <div className="app-page-actions-start">
              <button
                type="button"
                className="btn btn-ghost"
                disabled={savingDraft || finalizing || wizardBusy !== null}
                onClick={() => void onWizardBack()}
              >
                Back
              </button>
              <button
                type="button"
                className="btn btn-ghost"
                disabled={savingDraft || finalizing || wizardBusy !== null}
                onClick={() => void onDelete()}
              >
                Delete
              </button>
            </div>
            <div className="app-page-actions-main">
              {draft.hasGeneratedCv && (
                <button
                  type="button"
                  className="btn btn-ghost"
                  disabled={savingDraft || finalizing || wizardBusy !== null}
                  onClick={() => void onDownloadCv()}
                >
                  Download saved CV
                </button>
              )}
              <button
                type="button"
                className="btn btn-ghost"
                disabled={savingDraft || finalizing || wizardBusy !== null}
                onClick={() => void onSave()}
              >
                {savingDraft && !finalizing ? "Saving…" : "Save"}
              </button>
              <button
                type="button"
                className="btn btn-primary"
                disabled={savingDraft || finalizing || wizardBusy !== null}
                onClick={() => void onSaveAndGenerate()}
              >
                {finalizing
                  ? "Generating…"
                  : draft.hasGeneratedCv
                    ? "Regenerate CV"
                    : "Generate CV"}
              </button>
            </div>
          </div>
        </>
      )}

    </div>
  );
}
