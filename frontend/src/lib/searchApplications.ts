import type { ApplicationExperienceSnippet, ApplicationItem } from "../api";

export type SearchMatch = {
  label: string;
  snippet: string;
};

export type ApplicationSearchHit = {
  application: ApplicationItem;
  matches: SearchMatch[];
};

const SNIPPET_RADIUS = 48;
const MAX_MATCHES = 3;

export function searchTerms(query: string): string[] {
  return query
    .trim()
    .toLowerCase()
    .split(/\s+/)
    .filter(Boolean);
}

export function searchApplications(applications: ApplicationItem[], query: string): ApplicationSearchHit[] {
  const terms = searchTerms(query);
  if (terms.length === 0) return [];

  return applications.flatMap((application) => {
    const covered = new Set<string>();
    const matches = collectMatches(application, terms, covered);
    if (!terms.every((term) => covered.has(term))) return [];
    return [{ application, matches: matches.slice(0, MAX_MATCHES) }];
  });
}

export function highlightSegments(text: string, terms: string[]): Array<{ text: string; match: boolean }> {
  const needles = [...terms].filter(Boolean).sort((a, b) => b.length - a.length);
  if (needles.length === 0 || text.length === 0) return [{ text, match: false }];

  const lower = text.toLowerCase();
  const segments: Array<{ text: string; match: boolean }> = [];
  let index = 0;

  while (index < text.length) {
    let foundAt = -1;
    let foundLength = 0;
    for (const needle of needles) {
      const at = lower.indexOf(needle, index);
      if (at === -1) continue;
      if (foundAt === -1 || at < foundAt || (at === foundAt && needle.length > foundLength)) {
        foundAt = at;
        foundLength = needle.length;
      }
    }

    if (foundAt === -1) {
      segments.push({ text: text.slice(index), match: false });
      break;
    }

    if (foundAt > index) segments.push({ text: text.slice(index, foundAt), match: false });
    segments.push({ text: text.slice(foundAt, foundAt + foundLength), match: true });
    index = foundAt + foundLength;
  }

  return segments;
}

function collectMatches(application: ApplicationItem, terms: string[], covered: Set<string>): SearchMatch[] {
  const matches: SearchMatch[] = [];
  pushMatch(matches, covered, "Company", application.company, terms);
  pushMatch(matches, covered, "Role", application.roleTitle, terms);
  pushMatch(matches, covered, "Notes", application.notes, terms);
  pushMatch(matches, covered, "CV file", application.cvFileName, terms);

  for (const experience of application.experienceSnippets ?? []) {
    const label = experienceLabel(experience);
    for (const skill of experience.skills ?? []) {
      pushMatch(matches, covered, `Skill · ${label}`, skill, terms);
    }
    pushMatch(matches, covered, label, experience.description, terms);
  }

  return matches;
}

function experienceLabel(experience: ApplicationExperienceSnippet): string {
  const title = experience.title?.trim();
  const company = experience.company?.trim();
  if (title && company) return `${title} at ${company}`;
  return title || company || "Experience";
}

function pushMatch(
  matches: SearchMatch[],
  covered: Set<string>,
  label: string,
  value: string | null | undefined,
  terms: string[]
) {
  const text = value?.replace(/\s+/g, " ").trim() ?? "";
  if (!text) return;

  const lower = text.toLowerCase();
  const found = terms.filter((term) => lower.includes(term));
  if (found.length === 0) return;

  for (const term of found) covered.add(term);
  const snippet = snippetAround(text, terms);
  if (snippet) matches.push({ label, snippet });
}

function snippetAround(text: string, terms: string[]): string | null {
  const lower = text.toLowerCase();
  let index = -1;
  let length = 0;
  for (const term of terms) {
    const found = lower.indexOf(term);
    if (found === -1) continue;
    if (index === -1 || found < index) {
      index = found;
      length = term.length;
    }
  }

  if (index === -1) return null;

  const start = Math.max(0, index - SNIPPET_RADIUS);
  const end = Math.min(text.length, index + length + SNIPPET_RADIUS);
  const prefix = start > 0 ? "…" : "";
  const suffix = end < text.length ? "…" : "";
  return `${prefix}${text.slice(start, end).trim()}${suffix}`;
}
