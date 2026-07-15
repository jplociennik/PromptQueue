export type PromptStatus = 'pending' | 'processing' | 'completed' | 'failed';

export interface CreatePromptsRequest {
  prompts: string[];
}

export interface CreatePromptsResponse {
  ids: string[];
  status: PromptStatus;
}

export interface PromptResponse {
  id: string;
  content: string;
  status: PromptStatus;
  result: string | null;
  errorMessage: string | null;
  createdAt: string;
  updatedAt: string;
}

export type ValidationErrors = Record<string, string[]>;
