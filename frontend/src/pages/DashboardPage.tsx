import { useEffect, useState, type FormEvent } from "react";
import {
  api,
  ENGAGEMENT_TYPES,
  type ApplicationDetail,
  type ApplicationItem,
  type CvItem,
  type DraftEducation,
  type DraftExperience,
  type EducationInput,
  type EducationRecord,
  type ExperienceInput,
  type WorkExperience
} from "../api";
import { useAuth } from "../auth/AuthContext";

function formatProfileDate(iso?: string | null) {
  if (!iso) return "—";
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return "—";
  return date.toLocaleDateString("en-GB", {
    month: "short",
    year: "numeric",
    timeZone: "UTC"
  });
}

function formatDateRange(start?: string | null, end?: string | null, isCurrent?: boolean) {
  const from = formatProfileDate(start);
  const to = isCurrent ? "Present" : formatProfileDate(end);
  return `${from} – ${to}`;
}

function toMonthInput(iso?: string | null): string {
  if (!iso) return "";
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return "";
  const year = date.getUTCFullYear();
  const month = String(date.getUTCMonth() + 1).padStart(2, "0");
  return `${year}-${month}`;
}

function fromMonthInput(value: string): string | null {
  const trimmed = value.trim();
  return trimmed ? `${trimmed}-01` : null;
}

function emptyExperienceForm(): ExperienceInput {
  return {
    company: "",
    title: "",
    location: "",
    startDate: "",
    endDate: "",
    isCurrent: false,
    engagementType: "Permanent",
    description: ""
  };
}

function toExperienceForm(item: WorkExperience): ExperienceInput {
  return {
    company: item.company,
    title: item.title,
    location: item.location ?? "",
    startDate: toMonthInput(item.startDate),
    endDate: toMonthInput(item.endDate),
    isCurrent: item.isCurrent,
    engagementType: item.engagementType || "Permanent",
    description: item.description ?? ""
  };
}

function emptyEducationForm(): EducationInput {
  return {
    institution: "",
    degree: "",
    fieldOfStudy: "",
    startDate: "",
    endDate: "",
    description: ""
  };
}

function toEducationForm(item: EducationRecord): EducationInput {
  return {
    institution: item.institution,
    degree: item.degree ?? "",
    fieldOfStudy: item.fieldOfStudy ?? "",
    startDate: toMonthInput(item.startDate),
    endDate: toMonthInput(item.endDate),
    description: item.description ?? ""
  };
}

function downloadBlob(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = fileName;
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  URL.revokeObjectURL(url);
}

type ModalStep = "input" | "preview";

export default function DashboardPage() {
  const { user, logout } = useAuth();
  const [cvs, setCvs] = useState<CvItem[]>([]);
  const [experience, setExperience] = useState<WorkExperience[]>([]);
  const [education, setEducation] = useState<EducationRecord[]>([]);
  const [contactEmail, setContactEmail] = useState("");
  const [contactPhone, setContactPhone] = useState("");
  const [savingContact, setSavingContact] = useState(false);
  const [applications, setApplications] = useState<ApplicationItem[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [uploading, setUploading] = useState(false);
  const [editingExperienceId, setEditingExperienceId] = useState<string | "new" | null>(null);
  const [experienceForm, setExperienceForm] = useState<ExperienceInput>(emptyExperienceForm());
  const [savingExperience, setSavingExperience] = useState(false);
  const [editingEducationId, setEditingEducationId] = useState<string | "new" | null>(null);
  const [educationForm, setEducationForm] = useState<EducationInput>(emptyEducationForm());
  const [savingEducation, setSavingEducation] = useState(false);

  const [modalOpen, setModalOpen] = useState(false);
  const [modalStep, setModalStep] = useState<ModalStep>("input");
  const [jobAd, setJobAd] = useState("");
  const [previewing, setPreviewing] = useState(false);
  const [savingDraft, setSavingDraft] = useState(false);
  const [finalizing, setFinalizing] = useState(false);
  const [draft, setDraft] = useState<ApplicationDetail | null>(null);
  const [aiEditExperienceId, setAiEditExperienceId] = useState<string | null>(null);
  const [aiEditPrompt, setAiEditPrompt] = useState("");
  const [aiEditingId, setAiEditingId] = useState<string | null>(null);

  async function load() {
    try {
      const [cvList, appList, profile] = await Promise.all([
        api.listCvs(),
        api.listApplications(),
        api.getProfile()
      ]);
      setCvs(cvList);
      setApplications(appList);
      setContactEmail(profile.email ?? "");
      setContactPhone(profile.phone ?? "");
      setExperience(profile.experience);
      setEducation(profile.education);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load dashboard");
    }
  }

  useEffect(() => {
    void load();
  }, []);

  function openNewApplication() {
    setDraft(null);
    setJobAd("");
    setModalStep("input");
    setModalOpen(true);
    setError(null);
  }

  function closeModal() {
    setModalOpen(false);
    setDraft(null);
    setJobAd("");
    setModalStep("input");
    closeAiEdit();
  }

  async function openExistingApplication(id: string) {
    setError(null);
    try {
      const detail = await api.getApplication(id);
      setDraft(detail);
      setJobAd(detail.jobAd ?? "");
      setModalStep(detail.experiences.length > 0 || detail.education.length > 0 ? "preview" : "input");
      setModalOpen(true);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not open application");
    }
  }

  async function onUpload(fileList: FileList | null) {
    const file = fileList?.[0];
    if (!file) return;
    setUploading(true);
    setError(null);
    try {
      await api.uploadCv(file);
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Upload failed");
    } finally {
      setUploading(false);
    }
  }

  async function onCreatePreview(event: FormEvent) {
    event.preventDefault();
    setPreviewing(true);
    setError(null);
    try {
      const detail = await api.previewApplication(jobAd);
      setDraft(detail);
      setModalStep("preview");
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not create preview");
    } finally {
      setPreviewing(false);
    }
  }

  function updateDraftExperience(id: string, patch: Partial<DraftExperience>) {
    setDraft((current) => {
      if (!current) return current;
      return {
        ...current,
        experiences: current.experiences.map((exp) => (exp.id === id ? { ...exp, ...patch } : exp))
      };
    });
  }

  function updateDraftEducation(id: string, patch: Partial<DraftEducation>) {
    setDraft((current) => {
      if (!current) return current;
      return {
        ...current,
        education: current.education.map((edu) => (edu.id === id ? { ...edu, ...patch } : edu))
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

  async function onSaveAndClose() {
    if (!draft) {
      closeModal();
      return;
    }
    setSavingDraft(true);
    setError(null);
    try {
      await persistDraft();
      closeModal();
      await load();
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
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not generate CV");
    } finally {
      setSavingDraft(false);
      setFinalizing(false);
    }
  }

  async function onDownloadApplicationCv(id: string) {
    setError(null);
    try {
      const { blob, fileName } = await api.downloadApplicationCv(id);
      downloadBlob(blob, fileName);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not download CV");
    }
  }

  async function onDeleteApplication(id: string) {
    if (!window.confirm("Delete this application? The saved draft and generated CV will be removed.")) {
      return;
    }
    setError(null);
    try {
      await api.deleteApplication(id);
      if (draft?.id === id) {
        closeModal();
      }
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not delete application");
    }
  }

  function startCreateExperience() {
    setEditingExperienceId("new");
    setExperienceForm(emptyExperienceForm());
  }

  async function onSaveContact(event: FormEvent) {
    event.preventDefault();
    setSavingContact(true);
    setError(null);
    try {
      const updated = await api.updateContact({
        email: contactEmail.trim(),
        phone: contactPhone.trim()
      });
      setContactEmail(updated.email ?? "");
      setContactPhone(updated.phone ?? "");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not save contact details");
    } finally {
      setSavingContact(false);
    }
  }

  function startEditExperience(item: WorkExperience) {
    setEditingExperienceId(item.id);
    setExperienceForm(toExperienceForm(item));
  }

  function cancelExperienceEdit() {
    setEditingExperienceId(null);
    setExperienceForm(emptyExperienceForm());
  }

  async function onSaveExperience(event: FormEvent) {
    event.preventDefault();
    setSavingExperience(true);
    setError(null);
    try {
      const payload: ExperienceInput = {
        company: experienceForm.company.trim(),
        title: experienceForm.title.trim(),
        location: experienceForm.location?.trim() || null,
        startDate: fromMonthInput(experienceForm.startDate ?? ""),
        endDate: experienceForm.isCurrent ? null : fromMonthInput(experienceForm.endDate ?? ""),
        isCurrent: experienceForm.isCurrent,
        engagementType: experienceForm.engagementType || "Permanent",
        description: experienceForm.description?.trim() || null
      };

      if (editingExperienceId === "new") {
        await api.createExperience(payload);
      } else if (editingExperienceId) {
        await api.updateExperience(editingExperienceId, payload);
      }

      cancelExperienceEdit();
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not save experience");
    } finally {
      setSavingExperience(false);
    }
  }

  async function onDeleteExperience(id: string) {
    if (!window.confirm("Delete this experience entry?")) return;
    setError(null);
    try {
      await api.deleteExperience(id);
      if (editingExperienceId === id) {
        cancelExperienceEdit();
      }
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not delete experience");
    }
  }

  function startCreateEducation() {
    setEditingEducationId("new");
    setEducationForm(emptyEducationForm());
  }

  function startEditEducation(item: EducationRecord) {
    setEditingEducationId(item.id);
    setEducationForm(toEducationForm(item));
  }

  function cancelEducationEdit() {
    setEditingEducationId(null);
    setEducationForm(emptyEducationForm());
  }

  async function onSaveEducation(event: FormEvent) {
    event.preventDefault();
    setSavingEducation(true);
    setError(null);
    try {
      const payload: EducationInput = {
        institution: educationForm.institution.trim(),
        degree: educationForm.degree?.trim() || null,
        fieldOfStudy: educationForm.fieldOfStudy?.trim() || null,
        startDate: fromMonthInput(educationForm.startDate ?? ""),
        endDate: fromMonthInput(educationForm.endDate ?? ""),
        description: educationForm.description?.trim() || null
      };

      if (editingEducationId === "new") {
        await api.createEducation(payload);
      } else if (editingEducationId) {
        await api.updateEducation(editingEducationId, payload);
      }

      cancelEducationEdit();
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not save education");
    } finally {
      setSavingEducation(false);
    }
  }

  async function onDeleteEducation(id: string) {
    if (!window.confirm("Delete this education entry?")) return;
    setError(null);
    try {
      await api.deleteEducation(id);
      if (editingEducationId === id) {
        cancelEducationEdit();
      }
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not delete education");
    }
  }

  return (
    <div className="dash">
      <header className="dash-header">
        <div>
          <p className="brand">Application Assistant</p>
          <p className="muted">Signed in as {user?.name ?? user?.email}</p>
        </div>
        <button type="button" className="btn btn-ghost" onClick={() => void logout()}>
          Sign out
        </button>
      </header>

      {error && <div className="banner error">{error}</div>}

      <section className="hero-actions">
        <label className="action-card">
          <span className="action-title">Upload your CV</span>
          <span className="action-copy">
            PDF or Word, up to 10 MB. We store the file and extract contact details, experience &amp;
            education.
          </span>
          <input
            type="file"
            accept=".pdf,.doc,.docx,application/pdf"
            disabled={uploading}
            onChange={(e) => void onUpload(e.target.files)}
          />
          <span className="btn btn-secondary">{uploading ? "Parsing…" : "Choose file"}</span>
        </label>

        <button type="button" className="action-card action-card-button" onClick={openNewApplication}>
          <span className="action-title">New application</span>
          <span className="action-copy">Paste a job ad, review the tailored draft, then download your CV.</span>
          <span className="btn btn-primary">Start</span>
        </button>
      </section>

      <section className="panel contact-panel">
        <div className="panel-header">
          <h2>Contact</h2>
        </div>
        <form className="form profile-form" onSubmit={(e) => void onSaveContact(e)}>
          <div className="form-row">
            <label>
              Email
              <input
                type="email"
                value={contactEmail}
                onChange={(e) => setContactEmail(e.target.value)}
                placeholder="you@example.com"
                autoComplete="email"
              />
            </label>
            <label>
              Phone
              <input
                type="tel"
                value={contactPhone}
                onChange={(e) => setContactPhone(e.target.value)}
                placeholder="+44 …"
                autoComplete="tel"
              />
            </label>
          </div>
          <div className="form-actions">
            <button type="submit" className="btn btn-primary" disabled={savingContact}>
              {savingContact ? "Saving…" : "Save contact"}
            </button>
          </div>
        </form>
      </section>

      <section className="grid-two">
        <div className="panel">
          <div className="panel-header">
            <h2>Experience</h2>
            <button type="button" className="btn btn-ghost btn-small" onClick={startCreateExperience}>
              Add
            </button>
          </div>

          {editingExperienceId !== null && (
            <form className="form profile-form" onSubmit={(e) => void onSaveExperience(e)}>
              <h3>{editingExperienceId === "new" ? "New experience" : "Edit experience"}</h3>
              <label>
                Company
                <input
                  value={experienceForm.company}
                  onChange={(e) => setExperienceForm((f) => ({ ...f, company: e.target.value }))}
                  required
                />
              </label>
              <label>
                Title
                <input
                  value={experienceForm.title}
                  onChange={(e) => setExperienceForm((f) => ({ ...f, title: e.target.value }))}
                  required
                />
              </label>
              <label>
                Location
                <input
                  value={experienceForm.location ?? ""}
                  onChange={(e) => setExperienceForm((f) => ({ ...f, location: e.target.value }))}
                />
              </label>
              <label>
                Engagement
                <select
                  value={experienceForm.engagementType || "Permanent"}
                  onChange={(e) =>
                    setExperienceForm((f) => ({ ...f, engagementType: e.target.value }))
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
                    value={experienceForm.startDate ?? ""}
                    onChange={(e) => setExperienceForm((f) => ({ ...f, startDate: e.target.value }))}
                  />
                </label>
                <label>
                  End
                  <input
                    type="month"
                    value={experienceForm.endDate ?? ""}
                    onChange={(e) => setExperienceForm((f) => ({ ...f, endDate: e.target.value }))}
                    disabled={experienceForm.isCurrent}
                  />
                </label>
              </div>
              <label className="checkbox-row">
                <input
                  type="checkbox"
                  checked={experienceForm.isCurrent}
                  onChange={(e) =>
                    setExperienceForm((f) => ({
                      ...f,
                      isCurrent: e.target.checked,
                      endDate: e.target.checked ? "" : f.endDate
                    }))
                  }
                />
                Current role
              </label>
              <label>
                Description
                <textarea
                  value={experienceForm.description ?? ""}
                  onChange={(e) => setExperienceForm((f) => ({ ...f, description: e.target.value }))}
                  rows={3}
                />
              </label>
              <div className="form-actions">
                <button type="button" className="btn btn-ghost" onClick={cancelExperienceEdit}>
                  Cancel
                </button>
                <button type="submit" className="btn btn-primary" disabled={savingExperience}>
                  {savingExperience ? "Saving…" : "Save"}
                </button>
              </div>
            </form>
          )}

          {experience.length === 0 ? (
            <p className="muted">Upload a CV or add experience manually.</p>
          ) : (
            <ul className="list">
              {experience.map((item) => (
                <li key={item.id}>
                  <div className="list-row">
                    <strong>
                      {item.title}
                      {item.company ? ` · ${item.company}` : ""}
                    </strong>
                    <div className="list-actions">
                      <button
                        type="button"
                        className="btn btn-ghost btn-small"
                        onClick={() => startEditExperience(item)}
                      >
                        Edit
                      </button>
                      <button
                        type="button"
                        className="btn btn-ghost btn-small"
                        onClick={() => void onDeleteExperience(item.id)}
                      >
                        Delete
                      </button>
                    </div>
                  </div>
                  <span className="muted">
                    {formatDateRange(item.startDate, item.endDate, item.isCurrent)}
                    {item.engagementType ? ` · ${item.engagementType}` : ""}
                    {item.location ? ` · ${item.location}` : ""}
                  </span>
                  {item.description && <span className="list-detail">{item.description}</span>}
                </li>
              ))}
            </ul>
          )}
        </div>

        <div className="panel">
          <div className="panel-header">
            <h2>Education</h2>
            <button type="button" className="btn btn-ghost btn-small" onClick={startCreateEducation}>
              Add
            </button>
          </div>

          {editingEducationId !== null && (
            <form className="form profile-form" onSubmit={(e) => void onSaveEducation(e)}>
              <h3>{editingEducationId === "new" ? "New education" : "Edit education"}</h3>
              <label>
                Institution
                <input
                  value={educationForm.institution}
                  onChange={(e) => setEducationForm((f) => ({ ...f, institution: e.target.value }))}
                  required
                />
              </label>
              <label>
                Degree
                <input
                  value={educationForm.degree ?? ""}
                  onChange={(e) => setEducationForm((f) => ({ ...f, degree: e.target.value }))}
                />
              </label>
              <label>
                Field of study
                <input
                  value={educationForm.fieldOfStudy ?? ""}
                  onChange={(e) => setEducationForm((f) => ({ ...f, fieldOfStudy: e.target.value }))}
                />
              </label>
              <div className="form-row">
                <label>
                  Start
                  <input
                    type="month"
                    value={educationForm.startDate ?? ""}
                    onChange={(e) => setEducationForm((f) => ({ ...f, startDate: e.target.value }))}
                  />
                </label>
                <label>
                  End
                  <input
                    type="month"
                    value={educationForm.endDate ?? ""}
                    onChange={(e) => setEducationForm((f) => ({ ...f, endDate: e.target.value }))}
                  />
                </label>
              </div>
              <label>
                Description
                <textarea
                  value={educationForm.description ?? ""}
                  onChange={(e) => setEducationForm((f) => ({ ...f, description: e.target.value }))}
                  rows={3}
                />
              </label>
              <div className="form-actions">
                <button type="button" className="btn btn-ghost" onClick={cancelEducationEdit}>
                  Cancel
                </button>
                <button type="submit" className="btn btn-primary" disabled={savingEducation}>
                  {savingEducation ? "Saving…" : "Save"}
                </button>
              </div>
            </form>
          )}

          {education.length === 0 ? (
            <p className="muted">Upload a CV or add education manually.</p>
          ) : (
            <ul className="list">
              {education.map((item) => (
                <li key={item.id}>
                  <div className="list-row">
                    <strong>
                      {item.institution}
                      {item.degree ? ` · ${item.degree}` : ""}
                    </strong>
                    <div className="list-actions">
                      <button
                        type="button"
                        className="btn btn-ghost btn-small"
                        onClick={() => startEditEducation(item)}
                      >
                        Edit
                      </button>
                      <button
                        type="button"
                        className="btn btn-ghost btn-small"
                        onClick={() => void onDeleteEducation(item.id)}
                      >
                        Delete
                      </button>
                    </div>
                  </div>
                  <span className="muted">
                    {formatDateRange(item.startDate, item.endDate)}
                    {item.fieldOfStudy ? ` · ${item.fieldOfStudy}` : ""}
                  </span>
                  {item.description && <span className="list-detail">{item.description}</span>}
                </li>
              ))}
            </ul>
          )}
        </div>
      </section>

      <section className="grid-two">
        <div className="panel">
          <h2>Your CVs</h2>
          {cvs.length === 0 ? (
            <p className="muted">No CVs uploaded yet.</p>
          ) : (
            <ul className="list">
              {cvs.map((cv) => (
                <li key={cv.id}>
                  <strong>{cv.fileName}</strong>
                  <span className="muted">
                    {(cv.sizeBytes / 1024).toFixed(1)} KB ·{" "}
                    {new Date(cv.uploadedAt).toLocaleDateString()}
                  </span>
                </li>
              ))}
            </ul>
          )}
        </div>

        <div className="panel">
          <h2>Applications</h2>
          {applications.length === 0 ? (
            <p className="muted">No applications yet. Start one above to save a draft.</p>
          ) : (
            <ul className="list">
              {applications.map((app) => (
                <li key={app.id}>
                  <div className="list-row">
                    <strong>
                      {app.roleTitle} · {app.company}
                    </strong>
                    <div className="list-actions">
                      {app.hasGeneratedCv && (
                        <button
                          type="button"
                          className="btn btn-ghost btn-small"
                          onClick={() => void onDownloadApplicationCv(app.id)}
                        >
                          Download CV
                        </button>
                      )}
                      <button
                        type="button"
                        className="btn btn-ghost btn-small"
                        onClick={() => void openExistingApplication(app.id)}
                      >
                        Open
                      </button>
                      <button
                        type="button"
                        className="btn btn-ghost btn-small"
                        onClick={() => void onDeleteApplication(app.id)}
                      >
                        Delete
                      </button>
                    </div>
                  </div>
                  <span className="muted">
                    {app.status}
                    {app.cvFileName ? ` · ${app.cvFileName}` : ""} ·{" "}
                    {new Date(app.updatedAt).toLocaleDateString()}
                  </span>
                </li>
              ))}
            </ul>
          )}
        </div>
      </section>

      {modalOpen && (
        <div className="modal-backdrop" role="presentation" onClick={closeModal}>
          <div
            className="modal"
            role="dialog"
            aria-modal="true"
            aria-labelledby="application-modal-title"
            onClick={(e) => e.stopPropagation()}
          >
            <div className="modal-header">
              <div>
                <h2 id="application-modal-title">
                  {modalStep === "input" ? "New application" : "Review tailored draft"}
                </h2>
                {draft && modalStep === "preview" && (
                  <p className="muted modal-subtitle">
                    {draft.roleTitle} · {draft.company}
                  </p>
                )}
              </div>
              <button type="button" className="btn btn-ghost btn-small" onClick={closeModal}>
                Close
              </button>
            </div>

            {modalStep === "input" ? (
              <form
                className="modal-form"
                onSubmit={(e) => void onCreatePreview(e)}
              >
                <div className="modal-body form">
                  <label>
                    Job ad
                    <textarea
                      value={jobAd}
                      onChange={(e) => setJobAd(e.target.value)}
                      rows={12}
                      required
                      placeholder="Paste the full job advertisement here…"
                    />
                  </label>
                  <p className="muted modal-hint">
                    Company and role are detected automatically. Experience titles and descriptions are
                    AI-tailored to the job ad — review and edit them before generating the CV.
                  </p>
                </div>
                <div className="modal-footer">
                  <div className="form-actions">
                    <button type="button" className="btn btn-ghost" onClick={closeModal}>
                      Cancel
                    </button>
                    <button type="submit" className="btn btn-primary" disabled={previewing}>
                      {previewing ? "Analyzing…" : "Create preview"}
                    </button>
                  </div>
                </div>
              </form>
            ) : (
              draft && (
                <>
                  <div className="modal-body">
                    <div className="modal-preview">
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
                    Summary
                    <textarea
                      value={draft.summary ?? ""}
                      onChange={(e) => setDraft({ ...draft, summary: e.target.value })}
                      rows={4}
                    />
                  </label>

                  <label>
                    Skills (comma-separated)
                    <input
                      value={draft.skills.join(", ")}
                      onChange={(e) =>
                        setDraft({
                          ...draft,
                          skills: e.target.value
                            .split(",")
                            .map((s) => s.trim())
                            .filter(Boolean)
                        })
                      }
                    />
                  </label>

                  <div className="draft-section">
                    <h3>Experience for CV</h3>
                    <p className="muted">Toggle roles, edit titles, dates, and descriptions before generating.</p>
                    <ul className="draft-list">
                      {draft.experiences.map((exp) => (
                        <li key={exp.id} className={exp.include ? undefined : "draft-excluded"}>
                          <label className="checkbox-row">
                            <input
                              type="checkbox"
                              checked={exp.include}
                              onChange={(e) => updateDraftExperience(exp.id, { include: e.target.checked })}
                            />
                            Include on CV
                            <span className="muted">· score {Math.round(exp.relevanceScore)}</span>
                          </label>
                          <div className="form-row">
                            <label>
                              Title
                              <input
                                value={exp.title}
                                onChange={(e) => updateDraftExperience(exp.id, { title: e.target.value })}
                              />
                            </label>
                            <label>
                              Company
                              <input
                                value={exp.company}
                                onChange={(e) => updateDraftExperience(exp.id, { company: e.target.value })}
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
                                updateDraftExperience(exp.id, { description: e.target.value || null })
                              }
                              rows={4}
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
                                    updateDraftEducation(edu.id, { degree: e.target.value || null })
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
                  </div>
                  <div className="modal-footer">
                  <div className="form-actions">
                    <button
                      type="button"
                      className="btn btn-ghost"
                      disabled={savingDraft || finalizing}
                      onClick={() => void onDeleteApplication(draft.id)}
                    >
                      Delete
                    </button>
                    {draft.hasGeneratedCv && (
                      <button
                        type="button"
                        className="btn btn-ghost"
                        disabled={savingDraft || finalizing}
                        onClick={() => void onDownloadApplicationCv(draft.id)}
                      >
                        Download saved CV
                      </button>
                    )}
                    <button
                      type="button"
                      className="btn btn-ghost"
                      disabled={savingDraft || finalizing}
                      onClick={() => void onSaveAndClose()}
                    >
                      {savingDraft && !finalizing ? "Saving…" : "Save & close"}
                    </button>
                    <button
                      type="button"
                      className="btn btn-primary"
                      disabled={savingDraft || finalizing}
                      onClick={() => void onSaveAndGenerate()}
                    >
                      {finalizing ? "Generating…" : draft.hasGeneratedCv ? "Regenerate CV" : "Generate CV"}
                    </button>
                  </div>
                  </div>
                </>
              )
            )}
          </div>
        </div>
      )}
    </div>
  );
}
