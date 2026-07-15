import type { PromptStatus } from '../../../api/types';
import styles from './StatusBadge.module.css';

const STATUS_LABELS: Record<PromptStatus, string> = {
  pending: 'Oczekuje',
  processing: 'Przetwarzanie',
  completed: 'Zakończono',
  failed: 'Błąd',
};

interface StatusBadgeProps {
  status: PromptStatus;
}

export function StatusBadge({ status }: StatusBadgeProps) {
  return <span className={`${styles.badge} ${styles[status]}`}>{STATUS_LABELS[status]}</span>;
}
