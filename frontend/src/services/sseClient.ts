import type { AgentEvent, AgentResponse, ChatRequest, StreamErrorSse } from '../models/types';
import { getAuthToken } from '../auth/authToken';

const BASE_URL = import.meta.env.VITE_API_BASE_URL || '';

export function streamChat(
  request: ChatRequest,
  onEvent: (event: AgentEvent) => void,
  onResponse: (response: AgentResponse) => void,
  onDone: () => void,
  onError: (error: Error) => void
): AbortController {
  const controller = new AbortController();

  fetch(`${BASE_URL}/api/chat/stream`, {
    method: 'POST',
    headers: (() => {
      const t = getAuthToken();
      const h: Record<string, string> = { 'Content-Type': 'application/json' };
      if (t) h.Authorization = `Bearer ${t}`;
      return h;
    })(),
    body: JSON.stringify(request),
    signal: controller.signal,
  })
    .then(async (response) => {
      if (!response.ok) {
        throw new Error(`Stream error: ${response.status}`);
      }

      const reader = response.body?.getReader();
      if (!reader) throw new Error('No response body');

      const decoder = new TextDecoder();
      let buffer = '';
      /** Must persist across TCP chunks — `event:` and `data:` can arrive in separate reads. */
      let currentEventType = '';

      while (true) {
        const { done, value } = await reader.read();
        if (done) break;

        buffer += decoder.decode(value, { stream: true });
        const lines = buffer.split('\n');
        buffer = lines.pop() || '';

        for (const line of lines) {
          if (line.startsWith('event: ')) {
            currentEventType = line.slice(7).trim();
            continue;
          }

          if (line.startsWith('data: ')) {
            const data = line.slice(6).trim();

            if (data === '[DONE]') {
              onDone();
              return;
            }

            try {
              const parsed = JSON.parse(data) as unknown;

              if (currentEventType === 'stream_error') {
                const se = parsed as StreamErrorSse;
                const msg =
                  typeof se?.message === 'string' && se.message.length > 0
                    ? se.message
                    : 'Stream error';
                onError(new Error(msg));
                currentEventType = '';
                continue;
              }

              const isAgentResponseShape =
                typeof parsed === 'object' &&
                parsed !== null &&
                'inScope' in parsed &&
                'finalRecommendation' in parsed;

              if (currentEventType === 'agent_event' || (!isAgentResponseShape && (parsed as AgentEvent).stepType)) {
                onEvent(parsed as AgentEvent);
              } else if (currentEventType === 'agent_response' || isAgentResponseShape) {
                onResponse(parsed as AgentResponse);
              }
            } catch {
              // skip malformed events
            }

            currentEventType = '';
          }
        }
      }

      onDone();
    })
    .catch((err) => {
      if (err.name !== 'AbortError') {
        onError(err);
      }
    });

  return controller;
}
