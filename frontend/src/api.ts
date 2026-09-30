const API_BASE = import.meta.env.VITE_API_BASE_URL ?? "";

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${API_BASE}${path}`, {
    credentials: "include",
    ...init,
    headers: {
      ...(init?.body instanceof FormData ? {} : { "Content-Type": "application/json" }),
      ...init?.headers
    }
  });

  if (!response.ok) {
    let message = `Request failed (${response.status})`;
    try {
      const data = (await response.json()) as { error?: string };
      if (data.error) message = data.error;
    } catch {
      /* ignore */
    }
    throw new Error(message);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

function fileNameFromContentDisposition(disposition: string): string | null {
  const encoded = /filename\*\s*=\s*(?:UTF-8''|utf-8'')([^;]+)/i.exec(disposition);
  if (encoded?.[1]) {
    const raw = encoded[1].trim().replace(/^"|"$/g, "");
    try {
      return decodeURIComponent(raw);
    } catch {
      return raw;
    }
  }

  const quoted = /filename\s*=\s*"([^"]+)"/i.exec(disposition);
  if (quoted?.[1]) return quoted[1].trim();

  const plain = /filename\s*=\s*([^;]+)/i.exec(disposition);
  const value = plain?.[1]?.trim().replace(/^"|"$/g, "");
  return value || null;
}

async function requestPdf(path: string, init?: RequestInit): Promise<{ blob: Blob; fileName: string }> {
  const response = await fetch(`${API_BASE}${path}`, {
    credentials: "include",
    ...init,
    headers: {
      "Content-Type": "application/json",
      ...init?.headers
    }
  });

  if (!response.ok) {
    let message = `Request failed (${response.status})`;
    try {
      const data = (await response.json()) as { error?: string };
      if (data.error) message = data.error;
    } catch {
      /* ignore */
    }
    throw new Error(message);
  }

  const disposition = response.headers.get("Content-Disposition") ?? "";
  const fileName = fileNameFromContentDisposition(disposition) || "cv_download.pdf";
  const blob = await response.blob();
  return { blob, fileName };
}

export type User = {
  id: string;
  email: string;
  name: string;
};

export type CvItem = {
  id: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  uploadedAt: string;
};

export type EngagementType = "Permanent" | "Contract";

export const ENGAGEMENT_TYPES: EngagementType[] = ["Permanent", "Contract"];

export type WorkExperience = {
  id: string;
  sourceCvId?: string | null;
  company: string;
  title: string;
  location?: string | null;
  startDate?: string | null;
  endDate?: string | null;
  isCurrent: boolean;
  engagementType: EngagementType | string;
  description?: string | null;
  skills: string[];
};

export type EducationRecord = {
  id: string;
  sourceCvId?: string | null;
  institution: string;
  degree?: string | null;
  fieldOfStudy?: string | null;
  startDate?: string | null;
  endDate?: string | null;
  description?: string | null;
};

export type Profile = {
  email: string;
  phone: string;
  credits: number;
  experience: WorkExperience[];
  education: EducationRecord[];
};

export type ApplicationItem = {
  id: string;
  company: string;
  roleTitle: string;
  status: string;
  notes?: string | null;
  cvId?: string | null;
  cvFileName?: string | null;
  hasGeneratedCv?: boolean;
  createdAt: string;
  updatedAt: string;
};

export type DraftExperience = {
  id: string;
  include: boolean;
  relevanceScore: number;
  company: string;
  title: string;
  location?: string | null;
  startDate?: string | null;
  endDate?: string | null;
  isCurrent: boolean;
  engagementType: EngagementType | string;
  description?: string | null;
  skills: string[];
};

export type DraftEducation = {
  id: string;
  include: boolean;
  institution: string;
  degree?: string | null;
  fieldOfStudy?: string | null;
  startDate?: string | null;
  endDate?: string | null;
  description?: string | null;
};

export type ApplicationDetail = ApplicationItem & {
  jobAd?: string | null;
  summary?: string | null;
  skills: string[];
  experiences: DraftExperience[];
  education: DraftEducation[];
};

export type ExperienceInput = {
  company: string;
  title: string;
  location?: string | null;
  startDate?: string | null;
  endDate?: string | null;
  isCurrent: boolean;
  engagementType: EngagementType | string;
  description?: string | null;
  skills?: string[];
};

export type EducationInput = {
  institution: string;
  degree?: string | null;
  fieldOfStudy?: string | null;
  startDate?: string | null;
  endDate?: string | null;
  description?: string | null;
};

export type UpdateDraftPayload = {
  company?: string;
  roleTitle?: string;
  notes?: string;
  summary?: string;
  skills?: string[];
  experiences?: DraftExperience[];
  education?: DraftEducation[];
};

export const api = {
  me: () => request<User>("/api/auth/me"),
  logout: () => request<{ ok: boolean }>("/api/auth/logout", { method: "POST" }),
  getProfile: () => request<Profile>("/api/profile"),
  updateContact: (payload: { email: string; phone: string }) =>
    request<{ email: string; phone: string }>("/api/profile/contact", {
      method: "PUT",
      body: JSON.stringify(payload)
    }),
  createExperience: (payload: ExperienceInput) =>
    request<WorkExperience>("/api/profile/experience", {
      method: "POST",
      body: JSON.stringify(payload)
    }),
  updateExperience: (id: string, payload: ExperienceInput) =>
    request<WorkExperience>(`/api/profile/experience/${id}`, {
      method: "PUT",
      body: JSON.stringify(payload)
    }),
  deleteExperience: (id: string) =>
    request<void>(`/api/profile/experience/${id}`, { method: "DELETE" }),
  createEducation: (payload: EducationInput) =>
    request<EducationRecord>("/api/profile/education", {
      method: "POST",
      body: JSON.stringify(payload)
    }),
  updateEducation: (id: string, payload: EducationInput) =>
    request<EducationRecord>(`/api/profile/education/${id}`, {
      method: "PUT",
      body: JSON.stringify(payload)
    }),
  deleteEducation: (id: string) =>
    request<void>(`/api/profile/education/${id}`, { method: "DELETE" }),
  listCvs: () => request<CvItem[]>("/api/cvs"),
  uploadCv: (file: File) => {
    const body = new FormData();
    body.append("file", file);
    return request<CvItem>("/api/cvs", { method: "POST", body });
  },
  listApplications: () => request<ApplicationItem[]>("/api/applications"),
  getApplication: (id: string) => request<ApplicationDetail>(`/api/applications/${id}`),
  previewApplication: (jobAd: string) =>
    request<ApplicationDetail>("/api/applications", {
      method: "POST",
      body: JSON.stringify({ jobAd })
    }),
  updateApplicationDraft: (id: string, payload: UpdateDraftPayload) =>
    request<ApplicationDetail>(`/api/applications/${id}`, {
      method: "PATCH",
      body: JSON.stringify(payload)
    }),
  refineExperience: (applicationId: string, experienceId: string, prompt: string) =>
    request<DraftExperience>(`/api/applications/${applicationId}/experiences/${experienceId}`, {
      method: "POST",
      body: JSON.stringify({ prompt })
    }),
  finalizeApplication: (id: string) =>
    requestPdf(`/api/applications/${id}`, { method: "POST", body: "{}" }),
  downloadApplicationCv: (id: string) => requestPdf(`/api/applications/${id}/cv`),
  deleteApplication: (id: string) =>
    request<void>(`/api/applications/${id}`, { method: "DELETE" }),
  loginUrl: () => {
    const returnUrl = encodeURIComponent(window.location.origin);
    return `${API_BASE}/api/auth/login?returnUrl=${returnUrl}`;
  }
};
