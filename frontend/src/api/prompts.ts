import { http } from './client/client';
import type { CreatePromptsRequest, CreatePromptsResponse, PromptResponse } from './types';

export async function createPrompts(prompts: string[], signal?: AbortSignal) {
  const { data } = await http.post<CreatePromptsResponse>(
    '/api/v1/prompts',
    { prompts } satisfies CreatePromptsRequest,
    { signal },
  );
  return data;
}

export async function getPrompts(signal?: AbortSignal) {
  const { data } = await http.get<PromptResponse[]>('/api/v1/prompts', { signal });
  return data;
}
