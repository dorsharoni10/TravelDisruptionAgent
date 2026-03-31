const SUGGESTED_PROMPTS = [
  {
    text: 'My flight UA234 from NYC to London was cancelled. What are my options?',
    tag: 'Flight Cancellation',
  },
  {
    text: 'There is a snowstorm in Chicago. Will my connecting flight be affected?',
    tag: 'Weather Disruption',
  },
  {
    text: 'Will I make my 10:00 meeting in London if my flight is delayed?',
    tag: 'Schedule Impact',
  },
  {
    text: 'My flight LH789 is delayed. Suggest an alternative plan.',
    tag: 'Alternative Planning',
  },
  {
    text: 'What is the weather like for flights to JFK today?',
    tag: 'Weather Check',
  },
  {
    text: 'Tell me a joke about airplanes.',
    tag: 'Out of Scope',
  },
];

interface SuggestedPromptsProps {
  onSelect: (prompt: string) => void;
  visible?: boolean;
}

export function SuggestedPrompts({ onSelect, visible = true }: SuggestedPromptsProps) {
  if (!visible) return null;

  return (
    <div className="space-y-3 animate-fade-in">
      <div>
        <p className="text-xs font-medium uppercase tracking-wider text-slate-400">
          Try an example scenario
        </p>
        <p className="mt-1.5 text-xs leading-relaxed text-slate-500">
          Clicking a card fills the chat box only — press Send when ready. Replace flight numbers and
          cities with real ones (use IATA airport codes like TLV, JFK, LHR) so live flight and
          weather tools return accurate data.
        </p>
      </div>
      <div className="grid grid-cols-1 gap-2 sm:grid-cols-2 lg:grid-cols-3">
        {SUGGESTED_PROMPTS.map(({ text, tag }) => (
          <button
            key={text}
            onClick={() => onSelect(text)}
            className="group relative rounded-lg border border-slate-200 bg-white px-4 py-3 text-left shadow-sm transition-all hover:border-primary-200 hover:shadow-md"
          >
            <span className="mb-1.5 inline-block rounded-full bg-slate-100 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-slate-500 transition-colors group-hover:bg-primary-50 group-hover:text-primary-600">
              {tag}
            </span>
            <p className="text-sm leading-snug text-slate-700 group-hover:text-slate-900">
              {text}
            </p>
          </button>
        ))}
      </div>
    </div>
  );
}
