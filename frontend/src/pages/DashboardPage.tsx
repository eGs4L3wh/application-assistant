import { useEffect, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { api, type ApplicationItem } from "../api";
import { useAuth } from "../auth/AuthContext";
import ApplicationList from "../components/ApplicationList";
import ApplicationSearchForm from "../components/ApplicationSearchForm";
import { downloadBlob } from "../lib/download";

const RECENT_APPLICATION_LIMIT = 3;

export default function DashboardPage() {
  const { user, logout } = useAuth();
  const navigate = useNavigate();
  const [experienceCount, setExperienceCount] = useState(0);
  const [credits, setCredits] = useState(0);
  const [applications, setApplications] = useState<ApplicationItem[]>([]);
  const [loadedOnce, setLoadedOnce] = useState(false);
  const [showAllApplications, setShowAllApplications] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [uploading, setUploading] = useState(false);

  async function load() {
    try {
      const [appList, profile] = await Promise.all([api.listApplications(), api.getProfile()]);
      setApplications(appList);
      setExperienceCount(profile.experience.length);
      setCredits(profile.credits);
      setLoadedOnce(true);
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

  async function onDownloadApplicationCv(id: string, savedName?: string | null) {
    setError(null);
    try {
      const { blob, fileName } = await api.downloadApplicationCv(id);
      downloadBlob(blob, savedName?.trim() || fileName);
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
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not delete application");
    }
  }

  const needsCv = experienceCount === 0;
  const visibleApplications = showAllApplications
    ? applications
    : applications.slice(0, RECENT_APPLICATION_LIMIT);
  const hasMoreApplications = applications.length > RECENT_APPLICATION_LIMIT;

  return (
    <div className="dash">
      <header className="dash-header">
        <div>
          <p className="brand">Application Assistant</p>
          <p className="muted">Signed in as {user?.name ?? user?.email}</p>
        </div>
        <ApplicationSearchForm />
        <div className="app-page-header-actions">
          <Link to="/profile" className="btn btn-ghost btn-small">
            Profile
          </Link>
          <button type="button" className="btn btn-ghost btn-small" onClick={() => void logout()}>
            Sign out
          </button>
        </div>
      </header>

      {error && <div className="banner error">{error}</div>}

      {!loadedOnce ? (
        error ? null : (
          <div className="busy-indicator" role="status" aria-label="Loading dashboard">
            <span className="spinner" aria-hidden="true" />
          </div>
        )
      ) : needsCv ? (
        <label className="action-card hero-primary">
          <span className="action-title">Upload your CV</span>
          <span className="action-copy">
            PDF or Word, up to 10 MB. We store the file and extract your work experience so you can
            start applying.
          </span>
          <input
            type="file"
            accept=".pdf,.doc,.docx,application/pdf"
            disabled={uploading}
            onChange={(e) => void onUpload(e.target.files)}
          />
          <span className="btn btn-primary">{uploading ? "Parsing…" : "Choose file"}</span>
        </label>
      ) : credits < 1 ? (
        <div className="action-card hero-primary action-card-unavailable">
          <span className="action-title">Start a new application</span>
          <span className="action-copy">You need a credit before you can start an application.</span>
        </div>
      ) : (
        <button
          type="button"
          className="action-card action-card-button hero-primary"
          onClick={() => navigate("/applications/new")}
        >
          <span className="action-title">Start a new application</span>
          <span className="action-copy">
            Paste a job ad, confirm the requirements, pick the roles to include, then download your CV.
            Uses 1 credit ({credits} left).
          </span>
          <span className="btn btn-primary">Start</span>
        </button>
      )}

      {loadedOnce && applications.length > 0 && (
        <section className="panel">
          <h2>Recent applications</h2>
          <ApplicationList
            applications={visibleApplications}
            onOpen={(id) => navigate(`/applications/${id}`)}
            onDownload={(id, savedName) => void onDownloadApplicationCv(id, savedName)}
            onDelete={(id) => void onDeleteApplication(id)}
          />
          {hasMoreApplications && (
            <div className="see-more">
              <button
                type="button"
                className="btn btn-ghost btn-small"
                onClick={() => setShowAllApplications((open) => !open)}
              >
                {showAllApplications ? "Show less" : "See more"}
              </button>
            </div>
          )}
        </section>
      )}
    </div>
  );
}
