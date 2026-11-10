import type { ReactNode } from "react";
import type { ApplicationItem } from "../api";

type ApplicationListProps = {
  applications: ApplicationItem[];
  onOpen: (id: string) => void;
  onDownload: (id: string, savedName?: string | null) => void;
  onDelete: (id: string) => void;
  details?: (app: ApplicationItem) => ReactNode;
};

export default function ApplicationList({
  applications,
  onOpen,
  onDownload,
  onDelete,
  details
}: ApplicationListProps) {
  return (
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
                  onClick={() => onDownload(app.id, app.cvFileName)}
                >
                  Download CV
                </button>
              )}
              <button type="button" className="btn btn-ghost btn-small" onClick={() => onOpen(app.id)}>
                Open
              </button>
              <button type="button" className="btn btn-ghost btn-small" onClick={() => onDelete(app.id)}>
                Delete
              </button>
            </div>
          </div>
          <span className="muted">
            {app.status}
            {app.cvFileName ? ` · ${app.cvFileName}` : ""} · {new Date(app.updatedAt).toLocaleDateString()}
          </span>
          {app.notes?.trim() ? <span className="list-detail list-notes">{app.notes}</span> : null}
          {details?.(app)}
        </li>
      ))}
    </ul>
  );
}
