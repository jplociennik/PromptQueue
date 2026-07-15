import { useReducer } from 'react';
import { createPrompts } from '../../api/prompts';
import { ApiError } from '../../api/client/client';
import type { ValidationErrors } from '../../api/types';

type SubmitState =
  | { status: 'idle' }
  | { status: 'submitting' }
  | { status: 'success'; count: number }
  | { status: 'error'; message: string; validationErrors?: ValidationErrors };

type SubmitAction =
  | { type: 'submit' }
  | { type: 'success'; count: number }
  | { type: 'error'; message: string; validationErrors?: ValidationErrors };

function submitReducer(_state: SubmitState, action: SubmitAction): SubmitState {
  switch (action.type) {
    case 'submit':
      return { status: 'submitting' };
    case 'success':
      return { status: 'success', count: action.count };
    case 'error':
      return { status: 'error', message: action.message, validationErrors: action.validationErrors };
  }
}

export function useCreatePrompts() {
  const [state, dispatch] = useReducer(submitReducer, { status: 'idle' });

  const submit = async (prompts: string[]): Promise<boolean> => {
    dispatch({ type: 'submit' });

    try {
      await createPrompts(prompts);
      dispatch({ type: 'success', count: prompts.length });
      return true;
    } 
    catch (error) {
      const message = error instanceof ApiError ? error.message : 'Nie udało się wysłać promptów.';
      const validationErrors = error instanceof ApiError ? error.validationErrors : undefined;
      dispatch({ type: 'error', message, validationErrors });
      return false;
    }
  };

  return { state, submit };
}
