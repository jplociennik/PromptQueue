import { useReducer } from 'react';
import type { PromptField } from '../../features/prompts/types';

export const isBlank = (value: string): boolean => value.trim().length === 0;

const createField = (): PromptField => ({ id: crypto.randomUUID(), value: '' });

export type PromptFieldsAction =
  | { type: 'add' }
  | { type: 'remove'; id: string }
  | { type: 'change'; id: string; value: string }
  | { type: 'reset' };

export function promptFieldsReducer(fields: PromptField[], action: PromptFieldsAction): PromptField[] {
  switch (action.type) {
    case 'add':
      return [...fields, createField()];
    case 'remove':
      // Zawsze zostaje ≥1 pole: pusta lista → pusty POST → 400. Ostatniego pola nie usuwamy.
      return fields.length <= 1 ? fields : fields.filter((field) => field.id !== action.id);
    case 'change':
      return fields.map((field) => (field.id === action.id ? { ...field, value: action.value } : field));
    case 'reset':
      return [createField()];
  }
}

export function usePromptFields() {
  const [fields, dispatch] = useReducer(promptFieldsReducer, undefined, () => [createField()]);

  return {
    fields,
    add: () => dispatch({ type: 'add' }),
    remove: (id: string) => dispatch({ type: 'remove', id }),
    change: (id: string, value: string) => dispatch({ type: 'change', id, value }),
    reset: () => dispatch({ type: 'reset' }),
  };
}
