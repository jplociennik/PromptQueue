import { act, cleanup, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { usePromptPolling } from './usePromptPolling';
import { getPrompts } from '../../api/prompts';
import { ApiError } from '../../api/client/client';
import type { PromptResponse } from '../../api/types';

vi.mock('../../api/prompts', () => ({ getPrompts: vi.fn() }));
const getPromptsMock = vi.mocked(getPrompts);

const prompt = (id: string, status: PromptResponse['status'] = 'pending'): PromptResponse => ({
  id,
  content: `content ${id}`,
  status,
  result: null,
  errorMessage: null,
  createdAt: '2026-07-15T00:00:00Z',
  updatedAt: '2026-07-15T00:00:00Z',
});

// Wypycha zaległe mikrozadania (rozwiązanie zamockowanego getPrompts) w obrębie act.
const flush = async () => {
  await act(async () => {});
};

describe('usePromptPolling', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    getPromptsMock.mockReset();
  });

  afterEach(() => {
    cleanup();
    vi.useRealTimers();
  });

  it('loads prompts on the first tick and reports success', async () => {
    getPromptsMock.mockResolvedValue([prompt('1')]);
    const { result } = renderHook(() => usePromptPolling());

    await flush();

    expect(getPromptsMock).toHaveBeenCalledTimes(1);
    expect(result.current.status).toBe('success');
    expect(result.current.prompts).toEqual([prompt('1')]);
  });

  it('reschedules the next poll after the interval', async () => {
    getPromptsMock.mockResolvedValue([prompt('1')]);
    renderHook(() => usePromptPolling());
    await flush();
    expect(getPromptsMock).toHaveBeenCalledTimes(1);

    await act(async () => {
      vi.advanceTimersByTime(2000);
    });

    expect(getPromptsMock).toHaveBeenCalledTimes(2);
  });

  it('keeps polling even when every prompt is completed (always-poll)', async () => {
    getPromptsMock.mockResolvedValue([prompt('1', 'completed')]);
    renderHook(() => usePromptPolling());
    await flush();
    expect(getPromptsMock).toHaveBeenCalledTimes(1);

    await act(async () => {
      vi.advanceTimersByTime(2000);
    });

    expect(getPromptsMock).toHaveBeenCalledTimes(2);
  });

  it('stops polling and aborts the in-flight request on unmount', async () => {
    getPromptsMock.mockResolvedValue([prompt('1')]);
    const { unmount } = renderHook(() => usePromptPolling());
    await flush();
    const callsBeforeUnmount = getPromptsMock.mock.calls.length;

    unmount();
    await act(async () => {
      vi.advanceTimersByTime(4000);
    });

    expect(getPromptsMock).toHaveBeenCalledTimes(callsBeforeUnmount);
    const signal = getPromptsMock.mock.calls[0]?.[0];
    expect(signal?.aborted).toBe(true);
  });

  it('refetches immediately when refetch is called', async () => {
    getPromptsMock.mockResolvedValue([prompt('1')]);
    const { result } = renderHook(() => usePromptPolling());
    await flush();
    expect(getPromptsMock).toHaveBeenCalledTimes(1);

    await act(async () => {
      result.current.refetch();
    });
    await flush();

    expect(getPromptsMock).toHaveBeenCalledTimes(2);
  });

  it('reports error while keeping the last prompts and then retries', async () => {
    getPromptsMock.mockResolvedValueOnce([prompt('1')]);
    const { result } = renderHook(() => usePromptPolling());
    await flush();
    expect(result.current.prompts).toEqual([prompt('1')]);

    getPromptsMock.mockRejectedValueOnce(new ApiError(500, 'Server error'));
    await act(async () => {
      vi.advanceTimersByTime(2000);
    });
    await flush();

    expect(result.current.status).toBe('error');
    expect(result.current.error).toBe('Server error');
    expect(result.current.prompts).toEqual([prompt('1')]);

    getPromptsMock.mockResolvedValueOnce([prompt('1', 'completed')]);
    await act(async () => {
      vi.advanceTimersByTime(2000);
    });
    await flush();

    expect(result.current.status).toBe('success');
  });
});
