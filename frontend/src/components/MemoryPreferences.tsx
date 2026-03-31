import type { UserPreferences } from '../models/types';

interface MemoryPreferencesProps {
  preferences: UserPreferences | null;
  loading: boolean;
  memoryUsed?: boolean;
}

export function MemoryPreferences({ preferences, loading, memoryUsed }: MemoryPreferencesProps) {
  return (
    <div className="rounded-xl border border-slate-200 bg-white shadow-sm">
      <div className="flex items-center justify-between border-b border-slate-100 px-4 py-2.5">
        <div className="flex items-center gap-2">
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className="h-4 w-4 text-slate-400">
            <path d="M10 8a3 3 0 1 0 0-6 3 3 0 0 0 0 6ZM3.465 14.493a1.23 1.23 0 0 0 .41 1.412A9.957 9.957 0 0 0 10 18c2.31 0 4.438-.784 6.131-2.1.43-.333.604-.903.408-1.41a7.002 7.002 0 0 0-13.074.003Z" />
          </svg>
          <h3 className="text-xs font-semibold text-slate-700">Preferences</h3>
        </div>
        {memoryUsed !== undefined && (
          <span className={`inline-flex items-center gap-1 rounded-md px-1.5 py-0.5 text-[10px] font-semibold ring-1 ring-inset ${
            memoryUsed
              ? 'bg-emerald-50 text-emerald-700 ring-emerald-600/20'
              : 'bg-slate-50 text-slate-400 ring-slate-200'
          }`}>
            <span className={`inline-block h-1.5 w-1.5 rounded-full ${memoryUsed ? 'bg-emerald-500' : 'bg-slate-300'}`} />
            {memoryUsed ? 'Active' : 'Default'}
          </span>
        )}
      </div>
      <div className="p-4">
        {loading ? (
          <div className="flex items-center gap-2 text-xs text-slate-400">
            <svg className="h-3 w-3 animate-spin" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
              <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
              <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
            </svg>
            Loading...
          </div>
        ) : preferences ? (
          <dl className="space-y-2 text-xs">
            <PrefRow label="Airline" value={preferences.preferredAirline} />
            <PrefRow label="Home Airport" value={preferences.homeAirport} />
            <PrefRow label="Risk Tolerance" value={preferences.riskTolerance} badge />
            <PrefRow label="Meeting Buffer" value={preferences.preferredMeetingBufferMinutes ? `${preferences.preferredMeetingBufferMinutes} min` : ''} />
            <PrefRow label="Remote Fallback" value={preferences.prefersRemoteFallback ? 'Yes' : 'No'} />
            <PrefRow label="Seat" value={preferences.seatPreference} />
            <PrefRow label="Loyalty" value={preferences.loyaltyProgram} />
            <PrefRow label="Max Budget" value={preferences.maxBudgetUsd ? `$${preferences.maxBudgetUsd.toLocaleString()}` : ''} />
            {preferences.preferredAirports.length > 0 && (
              <PrefRow label="Airports" value={preferences.preferredAirports.join(', ')} />
            )}
          </dl>
        ) : (
          <p className="text-xs text-slate-400">No preferences configured</p>
        )}
      </div>
    </div>
  );
}

function PrefRow({ label, value, badge }: { label: string; value: string; badge?: boolean }) {
  const displayValue = value || '—';

  const riskStyles: Record<string, string> = {
    safe: 'bg-emerald-50 text-emerald-700',
    moderate: 'bg-amber-50 text-amber-700',
    aggressive: 'bg-red-50 text-red-700',
  };

  return (
    <div className="flex items-baseline justify-between gap-2">
      <dt className="text-slate-400">{label}</dt>
      <dd className="text-right font-medium text-slate-700">
        {badge && value ? (
          <span className={`rounded px-1.5 py-0.5 text-[10px] font-semibold ${riskStyles[value] ?? 'bg-slate-50 text-slate-600'}`}>
            {displayValue}
          </span>
        ) : (
          displayValue
        )}
      </dd>
    </div>
  );
}
