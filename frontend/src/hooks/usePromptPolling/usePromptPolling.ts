import { useEffect, useReducer, useState } from 'react';
import { getPrompts } from '../../api/prompts';
import { ApiError } from '../../api/client/client';
import type { PromptResponse } from '../../api/types';

const POLL_INTERVAL_MS = 2000;

export type PollingStatus = 'loading' | 'success' | 'error';

interface PollingState {
  prompts: PromptResponse[];
  status: PollingStatus;
  error: string | null;
}

type PollingAction =
  | { type: 'success'; prompts: PromptResponse[] }
  | { type: 'error'; message: string };

const initialState: PollingState = { prompts: [], status: 'loading', error: null };

function pollingReducer(state: PollingState, action: PollingAction): PollingState {
  switch (action.type) {
    case 'success':
      return { prompts: action.prompts, status: 'success', error: null };
    case 'error':
      return { ...state, status: 'error', error: action.message };
  }
}

export function usePromptPolling() {
  const [state, dispatch] = useReducer(pollingReducer, initialState);
  const [trigger, setTrigger] = useState(0);
  const refetch = () => setTrigger((n) => n + 1);

  useEffect(() => {
    let cancelled = false;
    let timerId: ReturnType<typeof setTimeout>;
    const controller = new AbortController();

    const tick = async () => {
      try {
        const prompts = await getPrompts(controller.signal);
        if (cancelled) return;
        dispatch({ type: 'success', prompts });
        timerId = setTimeout(tick, POLL_INTERVAL_MS);
      } catch (error) {
        if (cancelled) return;
        const message =
          error instanceof ApiError && error.status !== 0
            ? error.message
            : 'Nie udało się pobrać promptów.';
        dispatch({ type: 'error', message });
        timerId = setTimeout(tick, POLL_INTERVAL_MS);
      }
    };

    tick();

    return () => {
      cancelled = true;
      controller.abort();
      clearTimeout(timerId);
    };
  }, [trigger]);

  return { prompts: state.prompts, status: state.status, error: state.error, refetch };
}
