export function formatProfileDate(iso?: string | null) {
  if (!iso) return "—";
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return "—";
  return date.toLocaleDateString("en-GB", {
    month: "short",
    year: "numeric",
    timeZone: "UTC"
  });
}

export function formatDateRange(start?: string | null, end?: string | null, isCurrent?: boolean) {
  const from = formatProfileDate(start);
  const to = isCurrent ? "Present" : formatProfileDate(end);
  return `${from} – ${to}`;
}

export function toMonthInput(iso?: string | null): string {
  if (!iso) return "";
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return "";
  const year = date.getUTCFullYear();
  const month = String(date.getUTCMonth() + 1).padStart(2, "0");
  return `${year}-${month}`;
}

export function fromMonthInput(value: string): string | null {
  const trimmed = value.trim();
  return trimmed ? `${trimmed}-01` : null;
}
