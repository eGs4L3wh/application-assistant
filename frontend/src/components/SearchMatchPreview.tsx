import { highlightSegments, type SearchMatch } from "../lib/searchApplications";

type SearchMatchPreviewProps = {
  matches: SearchMatch[];
  terms: string[];
};

export default function SearchMatchPreview({ matches, terms }: SearchMatchPreviewProps) {
  if (matches.length === 0) return null;

  return (
    <ul className="search-hits">
      {matches.map((match) => (
        <li key={`${match.label}:${match.snippet}`}>
          <span className="search-hit-label">{match.label}</span>
          <span className="search-hit-snippet">
            {highlightSegments(match.snippet, terms).map((segment, index) =>
              segment.match ? <mark key={index}>{segment.text}</mark> : <span key={index}>{segment.text}</span>
            )}
          </span>
        </li>
      ))}
    </ul>
  );
}
