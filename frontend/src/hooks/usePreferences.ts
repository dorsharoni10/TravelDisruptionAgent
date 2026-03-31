import { useCallback, useEffect, useState } from 'react';
import type { UserPreferences } from '../models/types';
import { api } from '../services/api';

export function usePreferences() {
  const [preferences, setPreferences] = useState<UserPreferences | null>(null);
  const [loading, setLoading] = useState(false);

  const loadPreferences = useCallback(async () => {
    setLoading(true);
    try {
      const prefs = await api.getPreferences();
      setPreferences(prefs);
    } catch {
      // preferences may not exist yet
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadPreferences();
  }, [loadPreferences]);

  const savePreferences = useCallback(
    async (prefs: Omit<UserPreferences, 'userId' | 'updatedAt'>) => {
      await api.updatePreferences(prefs);
      await loadPreferences();
    },
    [loadPreferences]
  );

  return { preferences, loading, savePreferences, refresh: loadPreferences };
}
