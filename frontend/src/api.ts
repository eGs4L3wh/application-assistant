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

export const api = {
  me: () => request<User>("/api/auth/me"),
  logout: () => request<{ ok: boolean }>("/api/auth/logout", { method: "POST" }),
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
  loginUrl: () => {
    const returnUrl = encodeURIComponent(window.location.origin);
    return `${API_BASE}/api/auth/login?returnUrl=${returnUrl}`;
  }
};
