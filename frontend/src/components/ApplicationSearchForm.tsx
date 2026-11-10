import { useState, type FormEvent } from "react";
import { useNavigate } from "react-router-dom";

type ApplicationSearchFormProps = {
  initialQuery?: string;
};

export default function ApplicationSearchForm({ initialQuery = "" }: ApplicationSearchFormProps) {
  const navigate = useNavigate();
  const [query, setQuery] = useState(initialQuery);
  const trimmed = query.trim();

  function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (!trimmed) return;
    navigate(`/search?q=${encodeURIComponent(trimmed)}`);
  }

  return (
    <form className="app-search" role="search" onSubmit={onSubmit}>
      <input
        type="search"
        name="q"
        value={query}
        onChange={(event) => setQuery(event.target.value)}
        placeholder="Search applications"
        aria-label="Search applications"
      />
      <button type="submit" className="btn btn-ghost btn-small" disabled={!trimmed}>
        Search
      </button>
    </form>
  );
}
