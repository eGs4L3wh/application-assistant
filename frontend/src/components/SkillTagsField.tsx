import { useState, type KeyboardEvent } from "react";

type SkillTagsFieldProps = {
  label?: string;
  value: string[];
  onChange: (skills: string[]) => void;
  placeholder?: string;
};

function normalizeTag(raw: string): string {
  return raw.trim().replace(/^,+|,+$/g, "").trim();
}

export function SkillTagsField({
  label = "Skills",
  value,
  onChange,
  placeholder = "Add a skill and press Enter"
}: SkillTagsFieldProps) {
  const [draft, setDraft] = useState("");

  function commit(raw: string) {
    const tag = normalizeTag(raw);
    if (!tag) return;
    const exists = value.some((s) => s.toLowerCase() === tag.toLowerCase());
    if (!exists) {
      onChange([...value, tag]);
    }
    setDraft("");
  }

  function remove(index: number) {
    onChange(value.filter((_, i) => i !== index));
  }

  function onKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === "Enter" || event.key === ",") {
      event.preventDefault();
      commit(draft);
      return;
    }

    if (event.key === "Backspace" && !draft && value.length > 0) {
      event.preventDefault();
      remove(value.length - 1);
    }
  }

  return (
    <label className="skill-tags-field">
      {label}
      <div className="skill-tags-editor">
        {value.map((skill, index) => (
          <span key={`${skill}-${index}`} className="skill-tag">
            {skill}
            <button
              type="button"
              className="skill-tag-remove"
              aria-label={`Remove ${skill}`}
              onClick={() => remove(index)}
            >
              ×
            </button>
          </span>
        ))}
        <input
          className="skill-tags-input"
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
          onKeyDown={onKeyDown}
          onBlur={() => commit(draft)}
          placeholder={value.length === 0 ? placeholder : "Add another…"}
        />
      </div>
    </label>
  );
}

export function SkillTags({ skills }: { skills: string[] }) {
  if (skills.length === 0) return null;
  return (
    <div className="skill-tags-list" aria-label="Skills">
      {skills.map((skill) => (
        <span key={skill} className="skill-tag skill-tag-static">
          {skill}
        </span>
      ))}
    </div>
  );
}
