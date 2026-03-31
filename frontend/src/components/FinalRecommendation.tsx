interface FinalRecommendationProps {
  recommendation: string;
  confidence: number;
  intent: string;
}

function ConfidenceBar({ value }: { value: number }) {
  const pct = Math.round(value * 100);
  let color = 'bg-emerald-500';
  let textColor = 'text-emerald-700';
  let label = 'High';

  if (pct < 50) {
    color = 'bg-red-500';
    textColor = 'text-red-700';
    label = 'Low';
  } else if (pct < 75) {
    color = 'bg-amber-500';
    textColor = 'text-amber-700';
    label = 'Medium';
  }

  return (
    <div className="flex items-center gap-3">
      <div className="h-2 flex-1 overflow-hidden rounded-full bg-slate-100">
        <div
          className={`h-full rounded-full transition-all duration-700 ease-out ${color}`}
          style={{ width: `${pct}%` }}
        />
      </div>
      <span className={`text-xs font-semibold ${textColor}`}>
        {pct}% {label}
      </span>
    </div>
  );
}

export function FinalRecommendation({ recommendation, confidence, intent }: FinalRecommendationProps) {
  if (!recommendation) return null;

  return (
    <div className="animate-slide-up rounded-xl border border-emerald-200 bg-gradient-to-br from-emerald-50/80 to-white shadow-sm">
      <div className="flex items-center justify-between border-b border-emerald-100 px-5 py-3">
        <div className="flex items-center gap-2">
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className="h-4 w-4 text-emerald-500">
            <path fillRule="evenodd" d="M18 10a8 8 0 1 1-16 0 8 8 0 0 1 16 0Zm-7-4a1 1 0 1 1-2 0 1 1 0 0 1 2 0ZM9 9a.75.75 0 0 0 0 1.5h.253a.25.25 0 0 1 .244.304l-.459 2.066A1.75 1.75 0 0 0 10.747 15H11a.75.75 0 0 0 0-1.5h-.253a.25.25 0 0 1-.244-.304l.459-2.066A1.75 1.75 0 0 0 9.253 9H9Z" clipRule="evenodd" />
          </svg>
          <h3 className="text-sm font-semibold text-emerald-800">Final Recommendation</h3>
        </div>
        {intent && (
          <span className="rounded-full bg-emerald-100 px-2.5 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-emerald-700">
            {intent}
          </span>
        )}
      </div>
      <div className="space-y-4 p-5">
        <p className="whitespace-pre-wrap text-sm leading-relaxed text-slate-800">
          {recommendation}
        </p>
        <div>
          <div className="mb-1.5 text-xs font-medium text-slate-500">Confidence</div>
          <ConfidenceBar value={confidence} />
        </div>
      </div>
    </div>
  );
}
