import { useEffect, useRef } from 'react';

interface InputAreaProps {
  message: string;
  onMessageChange: (message: string) => void;
  onSend: (message: string) => void;
  disabled?: boolean;
  /** Increment to move focus into the composer (e.g. after inserting an example prompt). */
  focusNonce?: number;
}

export function InputArea({
  message,
  onMessageChange,
  onSend,
  disabled,
  focusNonce = 0,
}: InputAreaProps) {
  const textareaRef = useRef<HTMLTextAreaElement>(null);

  useEffect(() => {
    if (focusNonce === 0) return;
    const ta = textareaRef.current;
    if (!ta) return;
    ta.focus();
    const len = ta.value.length;
    requestAnimationFrame(() => {
      ta.setSelectionRange(len, len);
    });
  }, [focusNonce]);

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (message.trim() && !disabled) {
      onSend(message.trim());
      onMessageChange('');
    }
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      handleSubmit(e);
    }
  };

  return (
    <form onSubmit={handleSubmit} className="relative">
      <div className="overflow-hidden rounded-xl border border-slate-200 bg-white shadow-sm transition-shadow focus-within:border-primary-300 focus-within:shadow-md focus-within:ring-4 focus-within:ring-primary-50">
        <textarea
          ref={textareaRef}
          value={message}
          onChange={(e) => onMessageChange(e.target.value)}
          onKeyDown={handleKeyDown}
          placeholder="Describe your travel disruption scenario..."
          disabled={disabled}
          rows={2}
          className="block w-full resize-none border-0 bg-transparent px-4 pb-2 pt-4 text-sm text-slate-900 placeholder:text-slate-400 focus:outline-none focus:ring-0 disabled:bg-slate-50 disabled:text-slate-500"
        />
        <div className="flex items-center justify-between border-t border-slate-100 px-3 py-2">
          <span className="text-xs text-slate-400">
            {disabled ? 'Processing your request...' : 'Press Enter to send'}
          </span>
          <button
            type="submit"
            disabled={disabled || !message.trim()}
            className="inline-flex items-center gap-1.5 rounded-lg bg-primary-600 px-4 py-1.5 text-xs font-medium text-white shadow-sm transition-all hover:bg-primary-700 focus:outline-none focus:ring-2 focus:ring-primary-500/50 disabled:cursor-not-allowed disabled:opacity-40"
          >
            {disabled ? (
              <>
                <svg className="h-3.5 w-3.5 animate-spin" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                  <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                  <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
                </svg>
                Processing
              </>
            ) : (
              <>
                Send
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" className="h-3.5 w-3.5">
                  <path d="M2.87 2.298a.75.75 0 0 0-.812.81l.72 4.523a.75.75 0 0 0 .724.623h4.998a.25.25 0 0 1 0 .5H3.502a.75.75 0 0 0-.724.623l-.72 4.523a.75.75 0 0 0 .812.81 27.218 27.218 0 0 0 12.506-6.867.75.75 0 0 0 0-1.086A27.218 27.218 0 0 0 2.87 2.298Z" />
                </svg>
              </>
            )}
          </button>
        </div>
      </div>
    </form>
  );
}
