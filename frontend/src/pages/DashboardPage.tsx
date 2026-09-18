import { useEffect, useState, type FormEvent } from "react";
import { api, type ApplicationItem, type CvItem } from "../api";
import { useAuth } from "../auth/AuthContext";

export default function DashboardPage() {
  const { user, logout } = useAuth();
  const [cvs, setCvs] = useState<CvItem[]>([]);
  const [applications, setApplications] = useState<ApplicationItem[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [uploading, setUploading] = useState(false);
  const [creating, setCreating] = useState(false);
  const [showNewApplication, setShowNewApplication] = useState(false);
  const [company, setCompany] = useState("");
  const [roleTitle, setRoleTitle] = useState("");
  const [notes, setNotes] = useState("");
  const [cvId, setCvId] = useState("");

  async function load() {
    try {
      const [cvList, appList] = await Promise.all([api.listCvs(), api.listApplications()]);
      setCvs(cvList);
      setApplications(appList);
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

  async function onCreateApplication(event: FormEvent) {
    event.preventDefault();
    setCreating(true);
    setError(null);
    try {
      await api.createApplication({
        company,
        roleTitle,
        notes: notes || undefined,
        cvId: cvId || undefined
      });
      setCompany("");
      setRoleTitle("");
      setNotes("");
      setCvId("");
      setShowNewApplication(false);
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not create application");
    } finally {
      setCreating(false);
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
          <span className="action-copy">PDF or Word, up to 10 MB.</span>
          <input
            type="file"
            accept=".pdf,.doc,.docx,application/pdf"
            disabled={uploading}
            onChange={(e) => void onUpload(e.target.files)}
          />
          <span className="btn btn-secondary">{uploading ? "Uploading…" : "Choose file"}</span>
        </label>

        <button
          type="button"
          className="action-card action-card-button"
          onClick={() => setShowNewApplication((v) => !v)}
        >
          <span className="action-title">New application</span>
          <span className="action-copy">Capture company, role, and link a CV.</span>
          <span className="btn btn-primary">Start</span>
        </button>
      </section>

      {showNewApplication && (
        <form className="panel form" onSubmit={(e) => void onCreateApplication(e)}>
          <h2>New application</h2>
          <label>
            Company
            <input value={company} onChange={(e) => setCompany(e.target.value)} required />
          </label>
          <label>
            Role title
            <input value={roleTitle} onChange={(e) => setRoleTitle(e.target.value)} required />
          </label>
          <label>
            CV
            <select value={cvId} onChange={(e) => setCvId(e.target.value)}>
              <option value="">None</option>
              {cvs.map((cv) => (
                <option key={cv.id} value={cv.id}>
                  {cv.fileName}
                </option>
              ))}
            </select>
          </label>
          <label>
            Notes
            <textarea value={notes} onChange={(e) => setNotes(e.target.value)} rows={3} />
          </label>
          <div className="form-actions">
            <button type="button" className="btn btn-ghost" onClick={() => setShowNewApplication(false)}>
              Cancel
            </button>
            <button type="submit" className="btn btn-primary" disabled={creating}>
              {creating ? "Saving…" : "Create"}
            </button>
          </div>
        </form>
      )}

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
