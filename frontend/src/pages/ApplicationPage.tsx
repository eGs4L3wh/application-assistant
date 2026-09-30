import { useEffect, useState, type FormEvent } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import {
  api,
  ENGAGEMENT_TYPES,
  type ApplicationDetail,
  type DraftEducation,
  type DraftExperience
} from "../api";
import { ExperienceTimeline } from "../components/ExperienceTimeline";
import { SkillTagsField } from "../components/SkillTagsField";
import { fromMonthInput, toMonthInput } from "../lib/dates";
import { downloadBlob } from "../lib/download";
import { useAuth } from "../auth/AuthContext";

export default function ApplicationPage() {
  const { id } = useParams<{ id: string }>();
  const isNew = !id || id === "new";
  const navigate = useNavigate();
  const { user, logout } = useAuth();

  const [jobAd, setJobAd] = useState("");
  const [draft, setDraft] = useState<ApplicationDetail | null>(null);
  const [loading, setLoading] = useState(() => !isNew);
  const [previewing, setPreviewing] = useState(false);
  const [savingDraft, setSavingDraft] = useState(false);
  const [finalizing, setFinalizing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [aiEditExperienceId, setAiEditExperienceId] = useState<string | null>(null);
  const [aiEditPrompt, setAiEditPrompt] = useState("");
  const [aiEditingId, setAiEditingId] = useState<string | null>(null);
  const [timelineEditId, setTimelineEditId] = useState<string | null>(null);
  const [credits, setCredits] = useState<number | null>(null);

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
      setLoading(false);
      return;
    }

    let cancelled = false;
    setLoading(true);
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

  async function onCreatePreview(event: FormEvent) {
    event.preventDefault();
    if (previewing || !jobAd.trim() || credits === null || credits < 1) return;
    setPreviewing(true);
    setError(null);
    try {
      const detail = await api.previewApplication(jobAd);
      navigate(`/applications/${detail.id}`, { replace: true });
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not create preview");
      setPreviewing(false);
    }
  }

  function updateDraftExperience(experienceId: string, patch: Partial<DraftExperience>) {
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

  function updateDraftEducation(educationId: string, patch: Partial<DraftEducation>) {
    setDraft((current) => {
      if (!current) return current;
      return {
        ...current,
        education: current.education.map((edu) =>
          edu.id === educationId ? { ...edu, ...patch } : edu
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

  const timelineEditExp =
    draft && timelineEditId
      ? (draft.experiences.find((e) => e.id === timelineEditId) ?? null)
      : null;

  return (
    <div className={`app-page${draft && !isNew && !loading ? " app-page-with-actions" : ""}`}>
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
      ) : isNew ? (
        <section className="panel app-page-panel">
          <div className="panel-header">
            <h2>New application</h2>
          </div>
          <form
            className={`form${previewing ? " form-busy" : ""}`}
            onSubmit={(e) => void onCreatePreview(e)}
            aria-busy={previewing}
          >
            <label>
              Job ad
              <textarea
                value={jobAd}
                onChange={(e) => setJobAd(e.target.value)}
                rows={16}
                required
                disabled={previewing || credits === 0}
                placeholder="Paste the full job advertisement here…"
              />
            </label>
            {credits === 0 && (
              <p className="muted">You need a credit before you can start an application.</p>
            )}
            {credits !== null && credits > 0 && (
              <p className="muted">Uses 1 credit. You have {credits} left.</p>
            )}
            {previewing && (
              <div className="busy-banner" role="status" aria-live="polite">
                <span className="spinner" aria-hidden="true" />
                <div>
                  <strong>Analyzing job ad…</strong>
                  <p className="muted">Tailoring your experience — this can take a moment.</p>
                </div>
              </div>
            )}
            <div className="form-actions">
              <Link
                to="/"
                className={`btn btn-ghost${previewing ? " is-disabled" : ""}`}
                aria-disabled={previewing}
                onClick={(e) => {
                  if (previewing) e.preventDefault();
                }}
              >
                Cancel
              </Link>
              <button type="submit" className="btn btn-primary" disabled={previewing || credits === null || credits < 1}>
                {previewing ? (
                  <>
                    <span className="spinner spinner-on-dark" aria-hidden="true" />
                    Analyzing…
                  </>
                ) : (
                  "Create preview"
                )}
              </button>
            </div>
          </form>
        </section>
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
              <div className="draft-editor">
              <div className="form-row">
                <label>
                  Company
                  <input
                    value={draft.company}
                    onChange={(e) => setDraft({ ...draft, company: e.target.value })}
                  />
                </label>
                <label>
                  Role title
                  <input
                    value={draft.roleTitle}
                    onChange={(e) => setDraft({ ...draft, roleTitle: e.target.value })}
                  />
                </label>
              </div>

              <label>
                Notes
                <textarea
                  value={draft.notes ?? ""}
                  onChange={(e) => setDraft({ ...draft, notes: e.target.value })}
                  rows={4}
                  placeholder="Outcome, interview notes, follow-ups, salary, contacts…"
                />
              </label>

              <label>
                Summary
                <textarea
                  value={draft.summary ?? ""}
                  onChange={(e) => setDraft({ ...draft, summary: e.target.value })}
                  rows={5}
                />
              </label>

              <SkillTagsField
                value={draft.skills}
                onChange={(skills) => setDraft({ ...draft, skills })}
              />

              <div className="draft-section">
                <h3>Experience for CV</h3>
                <p className="muted">
                  Toggle roles, edit titles, dates, and descriptions before generating.
                </p>
                <ExperienceTimeline
                  experiences={draft.experiences}
                  onChangeExperience={(experienceId, patch) =>
                    updateDraftExperience(experienceId, patch)
                  }
                  onSelectExperience={(experienceId) => {
                    setTimelineEditId(experienceId);
                    setError(null);
                    closeAiEdit();
                  }}
                />
                <ul className="draft-list">
                  {draft.experiences.map((exp) => (
                    <li key={exp.id} className={exp.include ? undefined : "draft-excluded"}>
                      <label className="checkbox-row">
                        <input
                          type="checkbox"
                          checked={exp.include}
                          onChange={(e) =>
                            updateDraftExperience(exp.id, { include: e.target.checked })
                          }
                        />
                        Include on CV
                        <span className="muted">· score {Math.round(exp.relevanceScore)}</span>
                      </label>
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
                      <label>
                        Location
                        <input
                          value={exp.location ?? ""}
                          onChange={(e) =>
                            updateDraftExperience(exp.id, { location: e.target.value || null })
                          }
                        />
                      </label>
                      <label>
                        Engagement
                        <select
                          value={exp.engagementType || "Permanent"}
                          onChange={(e) =>
                            updateDraftExperience(exp.id, { engagementType: e.target.value })
                          }
                        >
                          {ENGAGEMENT_TYPES.map((type) => (
                            <option key={type} value={type}>
                              {type}
                            </option>
                          ))}
                        </select>
                      </label>
                      <div className="form-row">
                        <label>
                          Start
                          <input
                            type="month"
                            value={toMonthInput(exp.startDate)}
                            onChange={(e) =>
                              updateDraftExperience(exp.id, {
                                startDate: fromMonthInput(e.target.value)
                              })
                            }
                          />
                        </label>
                        <label>
                          End
                          <input
                            type="month"
                            value={toMonthInput(exp.endDate)}
                            onChange={(e) =>
                              updateDraftExperience(exp.id, {
                                endDate: fromMonthInput(e.target.value)
                              })
                            }
                            disabled={exp.isCurrent}
                          />
                        </label>
                      </div>
                      <label className="checkbox-row">
                        <input
                          type="checkbox"
                          checked={exp.isCurrent ?? false}
                          onChange={(e) =>
                            updateDraftExperience(exp.id, {
                              isCurrent: e.target.checked,
                              endDate: e.target.checked ? null : exp.endDate
                            })
                          }
                        />
                        Current role
                      </label>
                      <SkillTagsField
                        value={exp.skills ?? []}
                        onChange={(skills) => updateDraftExperience(exp.id, { skills })}
                      />
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
                          rows={5}
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
              </div>

              {draft.education.length > 0 && (
                <div className="draft-section">
                  <h3>Education for CV</h3>
                  <ul className="draft-list">
                    {draft.education.map((edu) => (
                      <li key={edu.id} className={edu.include ? undefined : "draft-excluded"}>
                        <label className="checkbox-row">
                          <input
                            type="checkbox"
                            checked={edu.include}
                            onChange={(e) =>
                              updateDraftEducation(edu.id, { include: e.target.checked })
                            }
                          />
                          Include on CV
                        </label>
                        <div className="form-row">
                          <label>
                            Institution
                            <input
                              value={edu.institution}
                              onChange={(e) =>
                                updateDraftEducation(edu.id, { institution: e.target.value })
                              }
                            />
                          </label>
                          <label>
                            Degree
                            <input
                              value={edu.degree ?? ""}
                              onChange={(e) =>
                                updateDraftEducation(edu.id, {
                                  degree: e.target.value || null
                                })
                              }
                            />
                          </label>
                        </div>
                        <div className="form-row">
                          <label>
                            Start
                            <input
                              type="month"
                              value={toMonthInput(edu.startDate)}
                              onChange={(e) =>
                                updateDraftEducation(edu.id, {
                                  startDate: fromMonthInput(e.target.value)
                                })
                              }
                            />
                          </label>
                          <label>
                            End
                            <input
                              type="month"
                              value={toMonthInput(edu.endDate)}
                              onChange={(e) =>
                                updateDraftEducation(edu.id, {
                                  endDate: fromMonthInput(e.target.value)
                                })
                              }
                            />
                          </label>
                        </div>
                      </li>
                    ))}
                  </ul>
                </div>
              )}
            </div>
          </section>
          </div>

          <div className="app-page-actions">
            <button
              type="button"
              className="btn btn-ghost"
              disabled={savingDraft || finalizing}
              onClick={() => void onDelete()}
            >
              Delete
            </button>
            <div className="app-page-actions-main">
              {draft.hasGeneratedCv && (
                <button
                  type="button"
                  className="btn btn-ghost"
                  disabled={savingDraft || finalizing}
                  onClick={() => void onDownloadCv()}
                >
                  Download saved CV
                </button>
              )}
              <button
                type="button"
                className="btn btn-ghost"
                disabled={savingDraft || finalizing}
                onClick={() => void onSave()}
              >
                {savingDraft && !finalizing ? "Saving…" : "Save"}
              </button>
              <button
                type="button"
                className="btn btn-primary"
                disabled={savingDraft || finalizing}
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

      {timelineEditExp && (
          <div
            className="modal-backdrop"
            role="presentation"
            onClick={() => {
              setTimelineEditId(null);
              closeAiEdit();
            }}
          >
            <div
              className="modal modal-compact modal-description"
              role="dialog"
              aria-modal="true"
              aria-labelledby="timeline-edit-title"
              onClick={(e) => e.stopPropagation()}
            >
              <div className="modal-header">
                <div>
                  <h2 id="timeline-edit-title">{timelineEditExp.title || "Untitled role"}</h2>
                  <p className="muted modal-subtitle">
                    {timelineEditExp.company || "Unknown company"}
                    {timelineEditExp.include ? "" : " · excluded from CV"}
                  </p>
                </div>
                <button
                  type="button"
                  className="btn btn-ghost btn-small"
                  onClick={() => {
                    setTimelineEditId(null);
                    closeAiEdit();
                  }}
                >
                  Close
                </button>
              </div>

              <div className="modal-body form">
                <div className="field-with-action">
                  <div className="list-row">
                    <span className="field-label">Description</span>
                    <button
                      type="button"
                      className="btn btn-ghost btn-small"
                      onClick={() =>
                        aiEditExperienceId === timelineEditExp.id
                          ? closeAiEdit()
                          : openAiEdit(timelineEditExp.id)
                      }
                    >
                      {aiEditExperienceId === timelineEditExp.id ? "Cancel" : "AI edit"}
                    </button>
                  </div>
                  <textarea
                    value={timelineEditExp.description ?? ""}
                    onChange={(e) =>
                      updateDraftExperience(timelineEditExp.id, {
                        description: e.target.value || null
                      })
                    }
                    rows={12}
                  />
                  {aiEditExperienceId === timelineEditExp.id && (
                    <div className="ai-edit-row">
                      <input
                        value={aiEditPrompt}
                        onChange={(e) => setAiEditPrompt(e.target.value)}
                        placeholder="e.g. emphasise leadership and cloud delivery"
                        disabled={aiEditingId === timelineEditExp.id}
                        onKeyDown={(e) => {
                          if (e.key === "Enter") {
                            e.preventDefault();
                            void onAiEditExperience(timelineEditExp.id);
                          }
                        }}
                      />
                      <button
                        type="button"
                        className="btn btn-primary btn-small"
                        disabled={
                          aiEditingId === timelineEditExp.id || !aiEditPrompt.trim()
                        }
                        onClick={() => void onAiEditExperience(timelineEditExp.id)}
                      >
                        {aiEditingId === timelineEditExp.id ? "Editing…" : "Apply"}
                      </button>
                    </div>
                  )}
                </div>
              </div>

              <div className="modal-footer">
                <div className="form-actions">
                  <button
                    type="button"
                    className="btn btn-primary"
                    onClick={() => {
                      setTimelineEditId(null);
                      closeAiEdit();
                    }}
                  >
                    Done
                  </button>
                </div>
              </div>
            </div>
          </div>
      )}
    </div>
  );
}
