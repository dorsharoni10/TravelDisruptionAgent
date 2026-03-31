import type { ChatHistoryMessage } from '../models/types';

interface ChatHistoryPanelProps {
  messages: ChatHistoryMessage[];
  onClear?: () => void;
  clearDisabled?: boolean;
}

export function ChatHistoryPanel({ messages, onClear, clearDisabled }: ChatHistoryPanelProps) {
  if (messages.length === 0) return null;

  return (
    <section
      className="mb-4 animate-fade-in rounded-xl border border-slate-200 bg-slate-50/80 p-4 shadow-sm"
      aria-label="Conversation history"
    >
      <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
        <h3 className="text-xs font-semibold uppercase tracking-wide text-slate-500">
          Conversation history
        </h3>
        {onClear && (
          <button
            type="button"
            onClick={onClear}
            disabled={clearDisabled}
            className="rounded-lg border border-slate-200 bg-white px-3 py-1.5 text-xs font-medium text-slate-600 shadow-sm transition-colors hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-50"
          >
            Clean Conversation
          </button>
        )}
      </div>
      <ul className="max-h-80 space-y-3 overflow-y-auto pr-1 text-sm">
        {messages.map((m, i) => (
          <li
            key={i}
            className={`flex ${m.role === 'user' ? 'justify-end' : 'justify-start'}`}
          >
            <div
              className={
                m.role === 'user'
                  ? `max-w-[85%] rounded-lg rounded-br-sm border px-3 py-2 text-slate-800 ${
                      m.pending
                        ? 'border-dashed border-amber-200 bg-amber-50/80'
                        : 'border-primary-200 bg-primary-50'
                    }`
                  : 'max-w-[90%] rounded-lg rounded-bl-sm border border-slate-200 bg-white px-3 py-2 text-slate-700 shadow-sm'
              }
            >
              <span className="mb-1 block text-[10px] font-medium uppercase text-slate-400">
                {m.role === 'user'
                  ? m.pending
                    ? 'You — waiting for server'
                    : 'You'
                  : 'Assistant'}
              </span>
              <p className="whitespace-pre-wrap break-words">{m.content}</p>
            </div>
          </li>
        ))}
      </ul>
    </section>
  );
}
