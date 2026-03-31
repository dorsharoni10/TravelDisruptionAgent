const STORAGE_KEY = 'tda_prefs_anon_id';

/** 1–64 chars [a-zA-Z0-9_-] for header X-Anonymous-User-Id when Auth is off (preferences only). */
export function getOrCreateAnonymousPreferencesId(): string {
  try {
    const existing = localStorage.getItem(STORAGE_KEY);
    if (existing && /^[a-zA-Z0-9_-]{1,64}$/.test(existing)) return existing;
    const id = crypto.randomUUID().replace(/-/g, '');
    localStorage.setItem(STORAGE_KEY, id);
    return id;
  } catch {
    return 'default';
  }
}
