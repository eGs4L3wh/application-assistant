# Application Assistant

.NET API + React (TypeScript) for CV uploads and job applications, with Google SSO. Built for Railway.

## Stack

- **Backend:** ASP.NET Core 10 Web API, cookie auth + Google OAuth, MongoDB
- **AI:** `ApplicationAssistant.AI` (Gemini behind `ICvParser`) for CV experience/education extraction
- **Frontend:** React + TypeScript + Vite
- **Deploy:** single Docker image (API serves the SPA from `wwwroot`)

## Quick start

### 1. Google OAuth

1. [Google Cloud Console → Credentials](https://console.cloud.google.com/apis/credentials)
2. Create **OAuth client ID** → Web application
3. Authorized JavaScript origins: `http://localhost:5173`, `http://localhost:5080`
4. Authorized redirect URIs: `http://localhost:5080/signin-google`
5. Put Client ID / Secret in `backend/ApplicationAssistant.Api/appsettings.Development.json` (or env vars below)
6. Create a [Gemini API key](https://aistudio.google.com/apikey) and set `Gemini:ApiKey` in the same file (or `Gemini__ApiKey`)

### 2. MongoDB

Local default: `mongodb://localhost:27017` / database `application-assistant`.

```bash
# example with Docker
docker run -d --name aa-mongo -p 27017:27017 mongo:7
```

### 3. Backend

```bash
cd backend/ApplicationAssistant.Api
dotnet restore
dotnet run
```

API: http://localhost:5080

### Date migration

If experience/education dates were stored as strings (e.g. `"Jul 2025"`), convert them to Mongo `Date` values:

```bash
cd backend/ApplicationAssistant.Api
dotnet run -- migrate-dates
```

Rules: `"2025"` → 1 Jan 2025, `"Jul 2025"` → 1 Jul 2025, `"Present"` → null (+ `IsCurrent` on experience).

### 4. Frontend

```bash
cd frontend
npm install
npm run dev
```

UI: http://localhost:5173 (talks to the API via `VITE_API_BASE_URL`)

## Dashboard

After Google login, the home page is a dashboard with:

- **Upload your CV** — PDF / Word, stored per user
- **New application** — company, role, optional CV link, notes

## Environment variables

| Variable | Description |
|----------|-------------|
| `Authentication__Google__ClientId` | Google OAuth client ID |
| `Authentication__Google__ClientSecret` | Google OAuth client secret |
| `Gemini__ApiKey` | Gemini Developer API key (CV parsing) |
| `Gemini__Model` | Optional model id (default `gemini-2.5-flash`) |
| `ConnectionStrings__Mongo` | MongoDB connection URI |
| `Mongo__DatabaseName` | Database name (default `application-assistant`) |
| `Frontend__Origin` | Frontend origin for CORS (dev: `http://localhost:5173`; prod: your public URL) |
| `ASPNETCORE_URLS` | e.g. `http://0.0.0.0:8080` (Railway sets this) |

## Railway

1. Create a project from this repo
2. Use the root `Dockerfile` / `railway.toml` (one service)
3. Add a Railway **MongoDB** plugin and set `ConnectionStrings__Mongo` to the provided connection URL (often `MONGO_URL` — copy/reference it)
4. Optionally set `Mongo__DatabaseName`
5. Set Google credentials, `Gemini__ApiKey`, and `Frontend__Origin` to the public app URL
6. In Google Cloud, add production redirect URI: `https://<your-app>/signin-google`

Health check: `GET /api/health`
