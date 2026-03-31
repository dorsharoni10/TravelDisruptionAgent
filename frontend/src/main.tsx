import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { setAuthToken } from './auth/authToken';
import App from './App';
import './index.css';

async function fetchDevJwtIfEnabled(): Promise<void> {
  if (import.meta.env.VITE_AUTH_ENABLED !== 'true') return;

  const base = import.meta.env.VITE_API_BASE_URL || '';
  const sub = import.meta.env.VITE_AUTH_SUB || 'demo-user';

  try {
    const r = await fetch(`${base}/api/auth/dev-token`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ sub, name: 'Demo user' }),
    });
    if (!r.ok) {
      console.warn('[TravelDisruptionAgent] dev-token request failed:', r.status);
      return;
    }
    const j = (await r.json()) as { accessToken?: string };
    if (j.accessToken) setAuthToken(j.accessToken);
  } catch (e) {
    console.warn('[TravelDisruptionAgent] dev-token error', e);
  }
}

void fetchDevJwtIfEnabled().finally(() => {
  createRoot(document.getElementById('root')!).render(
    <StrictMode>
      <App />
    </StrictMode>
  );
});
