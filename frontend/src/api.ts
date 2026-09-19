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

export type WorkExperience = {
  id: string;
  sourceCvId?: string | null;
  company: string;
  title: string;
  location?: string | null;
  startDate?: string | null;
  endDate?: string | null;
  isCurrent: boolean;
  description?: string | null;
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
  createdAt: string;
  updatedAt: string;
};

export type ExperienceInput = {
  company: string;
  title: string;
  location?: string | null;
  startDate?: string | null;
  endDate?: string | null;
  isCurrent: boolean;
  description?: string | null;
};

export type EducationInput = {
  institution: string;
  degree?: string | null;
  fieldOfStudy?: string | null;
  startDate?: string | null;
  endDate?: string | null;
  description?: string | null;
};

export const api = {
  me: () => request<User>("/api/auth/me"),
  logout: () => request<{ ok: boolean }>("/api/auth/logout", { method: "POST" }),
  getProfile: () => request<Profile>("/api/profile"),
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
  createApplication: (payload: {
    company: string;
    roleTitle: string;
    notes?: string;
    cvId?: string;
  }) =>
    request<ApplicationItem>("/api/applications", {
      method: "POST",
      body: JSON.stringify(payload)
    }),
  generateCv: async (payload: {
    jobAd: string;
    company?: string;
    roleTitle?: string;
  }): Promise<{ blob: Blob; fileName: string }> => {
    const response = await fetch(`${API_BASE}/api/applications/generate-cv`, {
      method: "POST",
      credentials: "include",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload)
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
    const match = /filename="?([^";]+)"?/i.exec(disposition);
    const fileName = match?.[1] || "cv-tailored.pdf";
    const blob = await response.blob();
    return { blob, fileName };
  },
  loginUrl: () => {
    const returnUrl = encodeURIComponent(window.location.origin);
    return `${API_BASE}/api/auth/login?returnUrl=${returnUrl}`;
  }
};
