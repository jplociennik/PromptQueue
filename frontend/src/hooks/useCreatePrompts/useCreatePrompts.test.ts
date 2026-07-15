import { act, renderHook } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { useCreatePrompts } from './useCreatePrompts';
import { createPrompts } from '../../api/prompts';
import { ApiError } from '../../api/client/client';

vi.mock('../../api/prompts', () => ({ createPrompts: vi.fn() }));
const createPromptsMock = vi.mocked(createPrompts);

describe('useCreatePrompts', () => {
  beforeEach(() => {
    createPromptsMock.mockReset();
  });

  it('sets success state and returns true when the request succeeds', async () => {
    createPromptsMock.mockResolvedValue({ ids: ['a', 'b'], status: 'pending' });
    const { result } = renderHook(() => useCreatePrompts());

    let returned: boolean | undefined;
    await act(async () => {
      returned = await result.current.submit(['p1', 'p2']);
    });

    expect(returned).toBe(true);
    expect(result.current.state).toEqual({ status: 'success', count: 2 });
  });

  it('sets error state and returns false when the request fails with ApiError', async () => {
    const validationErrors = { prompts: ['At least one prompt is required.'] };
    createPromptsMock.mockRejectedValue(new ApiError(400, 'Validation failed', validationErrors));
    const { result } = renderHook(() => useCreatePrompts());

    let returned: boolean | undefined;
    await act(async () => {
      returned = await result.current.submit([]);
    });

    expect(returned).toBe(false);
    expect(result.current.state).toEqual({ status: 'error', message: 'Validation failed', validationErrors });
  });

  it('falls back to a generic message and returns false on a non-ApiError failure', async () => {
    createPromptsMock.mockRejectedValue(new Error('boom'));
    const { result } = renderHook(() => useCreatePrompts());

    let returned: boolean | undefined;
    await act(async () => {
      returned = await result.current.submit(['p1']);
    });

    expect(returned).toBe(false);
    expect(result.current.state).toEqual({ status: 'error', message: 'Nie udało się wysłać promptów.' });
  });
});
