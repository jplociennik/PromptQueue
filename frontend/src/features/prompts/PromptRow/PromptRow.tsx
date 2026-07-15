import type { PromptResponse } from '../../../api/types';
import { StatusBadge } from '../../../components/UI/StatusBadge/StatusBadge';
import styles from './PromptRow.module.css';

interface PromptRowProps {
  prompt: PromptResponse;
}

export function PromptRow({ prompt }: PromptRowProps) {
  return (
    <li className={styles.row}>
      <StatusBadge status={prompt.status} />
      <p className={styles.content}>{prompt.content}</p>
      {prompt.status === 'completed' && prompt.result && <pre className={styles.result}>{prompt.result}</pre>}
      {prompt.status === 'failed' && prompt.errorMessage && <p className={styles.error}>{prompt.errorMessage}</p>}
    </li>
  );
}
