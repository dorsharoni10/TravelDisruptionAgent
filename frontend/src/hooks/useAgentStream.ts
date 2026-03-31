import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { AgentEvent, AgentResponse, ChatHistoryMessage } from '../models/types';
import { api } from '../services/api';
import { streamChat } from '../services/sseClient';

const SESSION_STORAGE_KEY = 'tda_session_id';

function mapServerHistory(
  raw: AgentResponse['conversationHistory']
): ChatHistoryMessage[] {
  if (!raw?.length) return [];
  return raw.map((m) => {
    const r = (m.role ?? '').toLowerCase();
    const role: 'user' | 'assistant' = r === 'assistant' ? 'assistant' : 'user';
    return {
      role,
      content: m.content ?? '',
      timestampUtc: m.timestampUtc,
    };
  });
}

export function useAgentStream() {
  const [events, setEvents] = useState<AgentEvent[]>([]);
  const [response, setResponse] = useState<AgentResponse | null>(null);
  /** Last transcript from the server (after each completed response). */
  const [chatHistory, setChatHistory] = useState<ChatHistoryMessage[]>([]);
  /** Current user message while the server has not yet returned (not in store). */
  const [pendingUser, setPendingUser] = useState<string | null>(null);
  const [isStreaming, setIsStreaming] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const controllerRef = useRef<AbortController | null>(null);
  /** Server-issued session id; sent on follow-up turns until reset. */
  const sessionIdRef = useRef<string | null>(null);

  useEffect(() => {
    try {
      const saved = localStorage.getItem(SESSION_STORAGE_KEY);
      if (saved) sessionIdRef.current = saved;
    } catch {
      /* private mode / SSR */
    }
  }, []);

  const displayMessages = useMemo(() => {
    const list = [...chatHistory];
    if (pendingUser)
      list.push({ role: 'user', content: pendingUser, pending: true });
    return list;
  }, [chatHistory, pendingUser]);

  const sendMessage = useCallback((message: string, explicitSessionId?: string) => {
    setEvents([]);
    setResponse(null);
    setError(null);
    setIsStreaming(true);
    setPendingUser(message);

    const sessionId = explicitSessionId ?? sessionIdRef.current ?? undefined;

    controllerRef.current = streamChat(
      { message, sessionId },
      (event) => setEvents((prev) => [...prev, event]),
      (resp) => {
        if (resp.sessionId) {
          sessionIdRef.current = resp.sessionId;
          try {
            localStorage.setItem(SESSION_STORAGE_KEY, resp.sessionId);
          } catch {
            /* ignore */
          }
        }
        setResponse(resp);
        setChatHistory(mapServerHistory(resp.conversationHistory));
        setPendingUser(null);
      },
      () => setIsStreaming(false),
      (err) => {
        setError(err.message);
        setIsStreaming(false);
        setPendingUser(null);
      }
    );
  }, []);

  /**
   * Clears server session first; local state updates only if the server confirms.
   * Without a session id, only local pending/UI state is cleared.
   */
  const reset = useCallback(async () => {
    controllerRef.current?.abort();
    const sid = sessionIdRef.current;
    if (sid) {
      try {
        await api.clearChatSession(sid);
      } catch (e) {
        setError(
          e instanceof Error
            ? e.message
            : 'Could not clear the conversation on the server — history was not removed.'
        );
        return;
      }
    }

    sessionIdRef.current = null;
    try {
      localStorage.removeItem(SESSION_STORAGE_KEY);
    } catch {
      /* ignore */
    }
    setEvents([]);
    setResponse(null);
    setError(null);
    setIsStreaming(false);
    setChatHistory([]);
    setPendingUser(null);
  }, []);

  return {
    events,
    response,
    chatHistory,
    displayMessages,
    isStreaming,
    error,
    sendMessage,
    reset,
  };
}
