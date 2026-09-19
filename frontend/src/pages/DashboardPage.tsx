import { useEffect, useState, type FormEvent } from "react";
import {
  api,
  type ApplicationItem,
  type CvItem,
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

export default function DashboardPage() {
  const { user, logout } = useAuth();
  const [cvs, setCvs] = useState<CvItem[]>([]);
  const [experience, setExperience] = useState<WorkExperience[]>([]);
  const [education, setEducation] = useState<EducationRecord[]>([]);
  const [applications, setApplications] = useState<ApplicationItem[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [uploading, setUploading] = useState(false);
  const [creating, setCreating] = useState(false);
  const [showNewApplication, setShowNewApplication] = useState(false);
  const [company, setCompany] = useState("");
  const [roleTitle, setRoleTitle] = useState("");
  const [jobAd, setJobAd] = useState("");
  const [editingExperienceId, setEditingExperienceId] = useState<string | "new" | null>(null);
  const [experienceForm, setExperienceForm] = useState<ExperienceInput>(emptyExperienceForm());
  const [savingExperience, setSavingExperience] = useState(false);
  const [editingEducationId, setEditingEducationId] = useState<string | "new" | null>(null);
  const [educationForm, setEducationForm] = useState<EducationInput>(emptyEducationForm());
  const [savingEducation, setSavingEducation] = useState(false);

  async function load() {
    try {
      const [cvList, appList, profile] = await Promise.all([
        api.listCvs(),
        api.listApplications(),
        api.getProfile()
      ]);
      setCvs(cvList);
      setApplications(appList);
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

  async function onGenerateCv(event: FormEvent) {
    event.preventDefault();
    setCreating(true);
    setError(null);
    try {
      const { blob, fileName } = await api.generateCv({
        jobAd,
        company: company || undefined,
        roleTitle: roleTitle || undefined
      });
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement("a");
      anchor.href = url;
      anchor.download = fileName;
      document.body.appendChild(anchor);
      anchor.click();
      anchor.remove();
      URL.revokeObjectURL(url);
      setCompany("");
      setRoleTitle("");
      setJobAd("");
      setShowNewApplication(false);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not generate CV");
    } finally {
      setCreating(false);
    }
  }

  function startCreateExperience() {
    setEditingExperienceId("new");
    setExperienceForm(emptyExperienceForm());
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
            PDF or Word, up to 10 MB. We store the file and extract experience &amp; education.
          </span>
          <input
            type="file"
            accept=".pdf,.doc,.docx,application/pdf"
            disabled={uploading}
            onChange={(e) => void onUpload(e.target.files)}
          />
          <span className="btn btn-secondary">{uploading ? "Parsing…" : "Choose file"}</span>
        </label>

        <button
          type="button"
          className="action-card action-card-button"
          onClick={() => setShowNewApplication((v) => !v)}
        >
          <span className="action-title">New application</span>
          <span className="action-copy">Paste a job ad and download a tailored 2-page CV.</span>
          <span className="btn btn-primary">Start</span>
        </button>
      </section>

      {showNewApplication && (
        <form className="panel form" onSubmit={(e) => void onGenerateCv(e)}>
          <h2>Generate tailored CV</h2>
          <label>
            Job ad
            <textarea
              value={jobAd}
              onChange={(e) => setJobAd(e.target.value)}
              rows={8}
              required
              placeholder="Paste the full job advertisement here…"
            />
          </label>
          <label>
            Company
            <input
              value={company}
              onChange={(e) => setCompany(e.target.value)}
              placeholder="Optional — used in filename and header"
            />
          </label>
          <label>
            Role title
            <input
              value={roleTitle}
              onChange={(e) => setRoleTitle(e.target.value)}
              placeholder="Optional — used in filename and header"
            />
          </label>
          <div className="form-actions">
            <button type="button" className="btn btn-ghost" onClick={() => setShowNewApplication(false)}>
              Cancel
            </button>
            <button type="submit" className="btn btn-primary" disabled={creating}>
              {creating ? "Generating…" : "Generate CV"}
            </button>
          </div>
        </form>
      )}

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
            <p className="muted">No applications yet.</p>
          ) : (
            <ul className="list">
              {applications.map((app) => (
                <li key={app.id}>
                  <strong>
                    {app.roleTitle} · {app.company}
                  </strong>
                  <span className="muted">
                    {app.status}
                    {app.cvFileName ? ` · ${app.cvFileName}` : ""}
                  </span>
                </li>
              ))}
            </ul>
          )}
        </div>
      </section>
    </div>
  );
}
