import type { UserPreferences } from '../models/types';
import { getOrCreateAnonymousPreferencesId } from '../auth/anonymousPreferencesId';
import { authHeadersJson } from '../auth/authToken';

const BASE_URL = import.meta.env.VITE_API_BASE_URL || '';

const authEnabled = import.meta.env.VITE_AUTH_ENABLED === 'true';

function preferenceRequestHeaders(): Headers {
  const headers = new Headers(authHeadersJson());
  if (!authEnabled) {
    const anon = getOrCreateAnonymousPreferencesId();
    if (anon !== 'default') headers.set('X-Anonymous-User-Id', anon);
  }
  return headers;
}

async function request<T>(url: string, options?: RequestInit): Promise<T> {
  const headers = new Headers(authHeadersJson());
  if (options?.headers) {
    const extra = new Headers(options.headers);
    extra.forEach((value, key) => headers.set(key, value));
  }

  const response = await fetch(`${BASE_URL}${url}`, {
    ...options,
    headers,
  });

  if (!response.ok) {
    throw new Error(`API error: ${response.status} ${response.statusText}`);
  }

  return response.json();
}

export const api = {
  getPreferences: () =>
    request<UserPreferences>('/api/preferences', {
      headers: preferenceRequestHeaders(),
    }),

  updatePreferences: (prefs: Omit<UserPreferences, 'userId' | 'updatedAt'>) =>
    request<void>('/api/preferences', {
      method: 'PUT',
      headers: preferenceRequestHeaders(),
      body: JSON.stringify(prefs),
    }),

  /** Clears server-side session history. No-op if session never started. */
  clearChatSession: async (sessionId: string): Promise<void> => {
    const response = await fetch(`${BASE_URL}/api/chat/session`, {
      method: 'DELETE',
      headers: authHeadersJson(),
      body: JSON.stringify({ sessionId }),
    });
    if (!response.ok) {
      throw new Error(`Clear session failed: ${response.status} ${response.statusText}`);
    }
  },
};
