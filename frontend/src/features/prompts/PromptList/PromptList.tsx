import type { PromptResponse } from '../../../api/types';
import type { PollingStatus } from '../../../hooks/usePromptPolling/usePromptPolling';
import { Alert } from '../../../components/UI/Alert/Alert';
import { PromptRow } from '../PromptRow/PromptRow';
import styles from './PromptList.module.css';

interface PromptListProps {
  prompts: PromptResponse[];
  status: PollingStatus;
  error: string | null;
}

export function PromptList({ prompts, status, error }: PromptListProps) {
  if (status === 'loading') return <p className={styles.info}>Ładowanie promptów…</p>;

  return (
    <section className={styles.list}>
      {status === 'error' && <Alert variant="error">{error}</Alert>}
      {prompts.length === 0 ? (
        status !== 'error' && <p className={styles.info}>Brak promptów.</p>
      ) : (
        <ul className={styles.items}>
          {prompts.map((prompt) => (
            <PromptRow key={prompt.id} prompt={prompt} />
          ))}
        </ul>
      )}
    </section>
  );
}
