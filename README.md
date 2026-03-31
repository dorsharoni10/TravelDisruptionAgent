# Travel Disruption Agent

Demo AI agent for business-travel disruptions: routing, planning, flight/weather tools (or mocks), policy RAG, self-correction, guardrails, and **SSE** live activity in the UI. Development defaults keep **auth off**; production startup validates JWT, CORS, and hosts (see `ValidateProductionConfiguration` in `Program.cs`).

**Conversation history:** local **MongoDB** (`mongodb://localhost:27017`) by default, or **in-memory** if `MongoDb__ConnectionString` is empty.

---

## Run from scratch (install → run)

Use **two terminals** when developing: one for the API, one for the UI.

### 1. Install prerequisites

| Tool | Why | Verify |
|------|-----|--------|
| [**Git**](https://git-scm.com/downloads) | Clone the repository | `git --version` |
| [**.NET 8 SDK**](https://dotnet.microsoft.com/download/dotnet/8.0) | Build and run the API | `dotnet --version` (expect **8.x**) |
| [**Node.js 18+**](https://nodejs.org/) (includes **npm**) | Frontend dev server and build | `node -v`, `npm -v` |

### 2. Clone and enter the repo

```bash
git clone <repository-url>
cd TravelDisruptionAgent
```

### 3. MongoDB (choose one)

- **A — Default (persistent sessions):** install MongoDB and run it on **`localhost:27017`** (matches `appsettings.json`). On Windows: e.g. `winget install MongoDB.Server`, then start the **MongoDB** service.
- **B — No MongoDB:** use the **in-memory** session store by setting an empty connection string before starting the API, e.g. PowerShell `$env:MongoDb__ConnectionString=""` or bash `export MongoDb__ConnectionString=`

### 4. Optional: API keys

The app runs **without** keys using mocks/fallbacks where implemented. For live LLM, weather, and flights, set variables from **`backend/src/TravelDisruptionAgent.Api/env.example`** (never commit real keys), e.g.:

```powershell
$env:Llm__ApiKey = "your-key"
$env:WeatherApi__ApiKey = "..."
$env:AviationStack__ApiKey = "..."
```

### 5. Backend — terminal 1

```bash
cd backend
dotnet restore
dotnet build
dotnet run --project src/TravelDisruptionAgent.Api
```

- API: **`http://localhost:5205`** · **`GET /health`** → `Healthy` · Development: **`/swagger`**

### 6. Frontend — terminal 2

```bash
cd frontend
npm install
npm run dev
```

- UI: **`http://localhost:5173`** — Vite proxies **`/api`** to the API (`frontend/vite.config.ts`).

Optional: `cp frontend/.env.example frontend/.env`. If **`Auth:Enabled`** on the API, set **`VITE_AUTH_ENABLED=true`** (or store a JWT in `sessionStorage` as `tda_jwt`).

### 7. Tests (optional)

```bash
cd backend
dotnet test
```

Evaluation only: `dotnet test --filter "FullyQualifiedName~EvaluationTests"`

---

## Main endpoints

| Method | Path | Notes |
|--------|------|--------|
| POST | `/api/chat/stream` | Chat + **SSE** |
| DELETE | `/api/chat/session` | Clear history — body `{ "sessionId": "..." }` |
| GET / PUT | `/api/preferences` | With auth: JWT user. Without auth: optional **`X-Anonymous-User-Id`** header, else shared **`default`** user |
| POST | `/api/auth/dev-token` | **Development only** — mint JWT when `Auth:AllowDevTokenEndpoint=true` |

---

## Environment variables

Full list: **`backend/src/TravelDisruptionAgent.Api/env.example`**. **Do not commit API keys.**

| Variable | Role |
|----------|------|
| `Llm__ApiKey` | LLM provider (empty → fallbacks where implemented) |
| `WeatherApi__ApiKey`, `WeatherApi__UseMock` | Real / mock weather |
| `AviationStack__ApiKey`, `AviationStack__UseMock` | Real / mock flights |
| `MongoDb__ConnectionString` | Empty → in-memory sessions |
| `Auth__Enabled`, `Auth__Jwt__SecretKey` (≥32 chars) | JWT when auth is on |
| `Auth__AllowDevTokenEndpoint` | `true` only in dev for `/api/auth/dev-token` |
| `AllowedOrigins__0`, … | Production: explicit SPA origin (not `*`) |
| `AllowedHosts` | Production: not `*` |

**Secrets:** use env vars or your host secret store; see `env.example` for PowerShell/bash examples.

---

## Architecture (summary)

```
React → POST /api/chat/stream (SSE) → AgentOrchestrator
  → router, memory, planning, tools, RAG, recommendation, self-correction, guardrails
```

**Layers:** Domain ← Application ← Infrastructure; API wires DI.

**SSE:** `agent_event` steps, then `agent_response`; on failure `stream_error` and `[DONE]`.

**Stack:** [Semantic Kernel](https://github.com/microsoft/semantic-kernel) for prompts; deterministic fallbacks when no LLM key is configured.

---

## Demo prompts (UI)

| Idea | What to expect |
|------|----------------|
| Flight number + status | Flight lookup |
| Cancellation + options + route | Route / alternatives when tools align |
| Storm at **ORD** | Weather + disruption-style answer |
| Company policy | RAG |
| Airplane joke | Out of scope |

---

## Project layout

```
TravelDisruptionAgent/
├── backend/src/TravelDisruptionAgent.{Api,Application,Domain,Infrastructure}/
├── backend/tests/TravelDisruptionAgent.Tests/
├── frontend/
└── README.md
```

---

## Limitations (course / homework scope)

- No full OIDC/SSO — symmetric JWT + dev token endpoint for local testing.
- **Auth and rate limiting are implemented** in this repo; the earlier “no auth” caveat does not apply.
- Not a full production setup (API gateway, managed secrets, full observability story).
- Without MongoDB or if the DB is down, session persistence may be lost or calls may fail depending on configuration.

Optional next steps: tighter RAG, external IdP, caching, Aspire dashboard.
