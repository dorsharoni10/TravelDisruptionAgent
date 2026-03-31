const STORAGE_KEY = 'tda_jwt';

export function getAuthToken(): string | null {
  try {
    return sessionStorage.getItem(STORAGE_KEY);
  } catch {
    return null;
  }
}

export function setAuthToken(token: string | null): void {
  try {
    if (token) sessionStorage.setItem(STORAGE_KEY, token);
    else sessionStorage.removeItem(STORAGE_KEY);
  } catch {
    /* private mode */
  }
}

/** Headers for JSON API calls when a JWT is present. */
export function authHeadersJson(): HeadersInit {
  const t = getAuthToken();
  if (!t) return { 'Content-Type': 'application/json' };
  return {
    'Content-Type': 'application/json',
    Authorization: `Bearer ${t}`,
  };
}
