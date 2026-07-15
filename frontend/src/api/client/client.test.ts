import { describe, expect, it } from 'vitest';
import { ApiError, toApiError } from './client';

// Typ argumentu toApiError (AxiosError<ProblemDetails>) bez eksportu wewnętrznego ProblemDetails.
type ToApiErrorArg = Parameters<typeof toApiError>[0];

const axiosError = (
  message: string,
  response?: { status: number; data?: { title?: string; errors?: Record<string, string[]> } },
): ToApiErrorArg => ({ message, response }) as unknown as ToApiErrorArg;

describe('toApiError', () => {
  it('maps a 400 problem+json response to ApiError with validationErrors', () => {
    const errors = {
      prompts: ['At least one prompt is required.'],
      'prompts[0]': ['Prompt must not be empty.'],
    };

    const result = toApiError(
      axiosError('Request failed with status code 400', { status: 400, data: { title: 'Validation failed', errors } }),
    );

    expect(result).toBeInstanceOf(ApiError);
    expect(result.status).toBe(400);
    expect(result.message).toBe('Validation failed');
    expect(result.validationErrors).toEqual(errors);
  });

  it('maps a 500 problem response to ApiError without validationErrors', () => {
    const result = toApiError(
      axiosError('Request failed with status code 500', { status: 500, data: { title: 'An unexpected error occurred.' } }),
    );

    expect(result.status).toBe(500);
    expect(result.message).toBe('An unexpected error occurred.');
    expect(result.validationErrors).toBeUndefined();
  });

  it('maps a network error without response to ApiError with status 0', () => {
    const result = toApiError(axiosError('Network Error'));

    expect(result.status).toBe(0);
    expect(result.message).toBe('Network Error');
    expect(result.validationErrors).toBeUndefined();
  });
});
