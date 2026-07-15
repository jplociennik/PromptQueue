import { usePromptPolling } from './hooks/usePromptPolling/usePromptPolling';
import { PromptForm } from './features/prompts/PromptForm/PromptForm';
import { PromptList } from './features/prompts/PromptList/PromptList';

export default function App() {
  const { prompts, status, error, refetch } = usePromptPolling();

  return (
    <main>
      <h1>PromptQueue</h1>
      <PromptForm onSubmitted={refetch} />
      <PromptList prompts={prompts} status={status} error={error} />
    </main>
  );
}
