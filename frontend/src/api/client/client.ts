import axios, { type AxiosError } from 'axios';
import type { ValidationErrors } from '../types';

export const http = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL ?? '',
  headers: { 'Content-Type': 'application/json' },
});

interface ProblemDetails {
  title?: string;
  errors?: ValidationErrors;
}

export class ApiError extends Error {
  constructor(
    readonly status: number,
    message: string,
    readonly validationErrors?: ValidationErrors,
  ) {
    super(message);
  }
}

// Mapuje AxiosError (z ProblemDetails backendu) na ApiError — czysta funkcja, testowalna bez sieci.
export function toApiError(error: AxiosError<ProblemDetails>): ApiError {
  const status = error.response?.status ?? 0;
  const message = error.response?.data?.title ?? error.message;
  const validationErrors = error.response?.data?.errors;
  return new ApiError(status, message, validationErrors);
}

http.interceptors.response.use(
  (response) => response,
  (error: AxiosError<ProblemDetails>) => Promise.reject(toApiError(error)),
);
