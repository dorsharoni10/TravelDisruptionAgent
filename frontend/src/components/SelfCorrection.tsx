import type { SelfCorrectionStep } from '../models/types';

interface SelfCorrectionProps {
  steps: SelfCorrectionStep[];
}

export function SelfCorrection({ steps }: SelfCorrectionProps) {
  return (
    <div className="animate-slide-up rounded-xl border border-purple-200 bg-gradient-to-br from-purple-50/60 to-white shadow-sm">
      <div className="flex items-center gap-2 border-b border-purple-100 px-5 py-3">
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className="h-4 w-4 text-purple-500">
          <path fillRule="evenodd" d="M15.312 11.424a5.5 5.5 0 0 1-9.201 2.466l-.312-.311h2.433a.75.75 0 0 0 0-1.5H4.598a.75.75 0 0 0-.75.75v3.634a.75.75 0 0 0 1.5 0v-2.033l.312.311a7 7 0 0 0 11.712-3.138.75.75 0 0 0-1.449-.39Zm1.23-3.723a.75.75 0 0 0 .219-.53V3.537a.75.75 0 0 0-1.5 0v2.033l-.312-.311a7 7 0 0 0-11.712 3.138.75.75 0 0 0 1.449.39 5.5 5.5 0 0 1 9.201-2.466l.312.311H11.77a.75.75 0 0 0 0 1.5h3.634a.75.75 0 0 0 .53-.219l.006-.006Z" clipRule="evenodd" />
        </svg>
        <h3 className="text-sm font-semibold text-purple-800">Self-Correction &amp; fallbacks</h3>
        <span className="rounded-full bg-purple-100 px-2 py-0.5 text-xs font-medium text-purple-700">
          {steps.length === 0 ? 'none this turn' : `${steps.length} step${steps.length === 1 ? '' : 's'}`}
        </span>
      </div>
      {steps.length === 0 ? (
        <p className="px-5 py-4 text-xs text-slate-600">
          No extra correction was required: tools matched expectations, or failures were already surfaced in tool cards
          above. When a tool fails or the answer disagrees with data, steps appear here (e.g. route fallback, scrubbing
          unverified claims).
        </p>
      ) : (
      <div className="divide-y divide-purple-100/60 p-2">
        {steps.map((step, i) => (
          <div key={i} className="rounded-lg p-3">
            <div className="flex items-start gap-3">
              <div className="mt-0.5 flex h-5 w-5 shrink-0 items-center justify-center rounded-full bg-purple-100 text-[10px] font-bold text-purple-700">
                {i + 1}
              </div>
              <div className="min-w-0 flex-1 space-y-1">
                <div className="text-sm font-medium text-slate-800">{step.issue}</div>
                <div className="flex items-start gap-1 text-xs text-slate-600">
                  <span className="shrink-0 font-semibold text-purple-600">Action:</span>
                  <span>{step.action}</span>
                </div>
                <div className="flex items-start gap-1 text-xs text-slate-600">
                  <span className="shrink-0 font-semibold text-emerald-600">Outcome:</span>
                  <span>{step.outcome}</span>
                </div>
              </div>
            </div>
          </div>
        ))}
      </div>
      )}
    </div>
  );
}
