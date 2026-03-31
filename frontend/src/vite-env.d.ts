/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_API_BASE_URL: string;
  /** When "true", fetches POST /api/auth/dev-token before render (requires backend Auth:Enabled + dev endpoint). */
  readonly VITE_AUTH_ENABLED: string;
  /** Subject claim for dev token (default demo-user). */
  readonly VITE_AUTH_SUB: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
