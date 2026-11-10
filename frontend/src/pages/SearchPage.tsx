import { useEffect, useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { api, type ApplicationItem } from "../api";
import { useAuth } from "../auth/AuthContext";
import ApplicationList from "../components/ApplicationList";
import ApplicationSearchForm from "../components/ApplicationSearchForm";
import SearchMatchPreview from "../components/SearchMatchPreview";
import { downloadBlob } from "../lib/download";
import { searchApplications, searchTerms } from "../lib/searchApplications";

export default function SearchPage() {
  const { user, logout } = useAuth();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const query = params.get("q")?.trim() ?? "";
  const [applications, setApplications] = useState<ApplicationItem[]>([]);
  const [loadedOnce, setLoadedOnce] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function load() {
    try {
      const appList = await api.listApplications();
      setApplications(appList);
      setLoadedOnce(true);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to search applications");
    }
  }

  useEffect(() => {
    void load();
  }, []);

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

  const results = query ? searchApplications(applications, query) : [];
  const terms = searchTerms(query);
  const resultLabel =
    results.length === 1 ? "1 application" : `${results.length} applications`;

  return (
    <div className="dash">
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

      <ApplicationSearchForm key={query} initialQuery={query} />

      {!loadedOnce ? (
        error ? null : (
          <div className="busy-indicator" role="status" aria-label="Searching applications">
            <span className="spinner" aria-hidden="true" />
          </div>
        )
      ) : (
        <section className="panel">
          <h2>{query ? `Results for “${query}”` : "Search applications"}</h2>
          {!query ? (
            <p className="muted">
              Search by company, role, notes, CV name, or the skills and descriptions on roles included in that application.
            </p>
          ) : results.length === 0 ? (
            <p className="muted">No applications match this search.</p>
          ) : (
            <>
              <p className="muted search-count">{resultLabel}</p>
              <ApplicationList
                applications={results.map((hit) => hit.application)}
                onOpen={(id) => navigate(`/applications/${id}`)}
                onDownload={(id, savedName) => void onDownloadApplicationCv(id, savedName)}
                onDelete={(id) => void onDeleteApplication(id)}
                details={(app) => (
                  <SearchMatchPreview
                    matches={results.find((hit) => hit.application.id === app.id)?.matches ?? []}
                    terms={terms}
                  />
                )}
              />
            </>
          )}
        </section>
      )}
    </div>
  );
}
