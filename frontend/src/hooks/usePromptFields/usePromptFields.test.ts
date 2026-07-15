import { describe, expect, it } from 'vitest';
import { isBlank, promptFieldsReducer } from './usePromptFields';
import type { PromptField } from '../../features/prompts/types';

const field = (id: string, value = ''): PromptField => ({ id, value });

describe('promptFieldsReducer', () => {
  it('add appends a new empty field', () => {
    const result = promptFieldsReducer([field('1', 'a')], { type: 'add' });

    expect(result).toHaveLength(2);
    expect(result[1]).toEqual({ id: expect.any(String), value: '' });
  });

  it('remove drops the field with the given id', () => {
    const result = promptFieldsReducer([field('1'), field('2')], { type: 'remove', id: '1' });

    expect(result.map((f) => f.id)).toEqual(['2']);
  });

  it('remove keeps the last field (never leaves an empty list)', () => {
    const result = promptFieldsReducer([field('1', 'a')], { type: 'remove', id: '1' });

    expect(result).toHaveLength(1);
    expect(result.map((f) => f.id)).toEqual(['1']);
  });

  it('change updates only the targeted field', () => {
    const result = promptFieldsReducer([field('1'), field('2')], { type: 'change', id: '2', value: 'hello' });

    expect(result.find((f) => f.id === '2')?.value).toBe('hello');
    expect(result.find((f) => f.id === '1')?.value).toBe('');
  });

  it('reset returns a single empty field', () => {
    const result = promptFieldsReducer([field('1', 'a'), field('2', 'b')], { type: 'reset' });

    expect(result).toHaveLength(1);
    expect(result[0]?.value).toBe('');
  });
});

describe('isBlank', () => {
  it('treats empty and whitespace-only strings as blank', () => {
    expect(isBlank('')).toBe(true);
    expect(isBlank('   ')).toBe(true);
  });

  it('treats strings with visible characters as not blank', () => {
    expect(isBlank('x')).toBe(false);
  });
});
