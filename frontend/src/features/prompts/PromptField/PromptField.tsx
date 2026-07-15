import { TextArea } from '../../../components/UI/TextArea/TextArea';
import { Button } from '../../../components/UI/Button/Button';
import styles from './PromptField.module.css';

interface PromptFieldProps {
  index: number;
  value: string;
  invalid: boolean;
  disabled: boolean;
  canRemove: boolean;
  onChange: (value: string) => void;
  onRemove: () => void;
}

export function PromptField({ index, value, invalid, disabled, canRemove, onChange, onRemove }: PromptFieldProps) {
  return (
    <div className={styles.field}>
      <TextArea
        aria-label={`Prompt ${index + 1}`}
        placeholder="Wpisz prompt…"
        value={value}
        invalid={invalid}
        disabled={disabled}
        onChange={(event) => onChange(event.target.value)}
      />
      <Button
        type="button"
        variant="ghost"
        aria-label={`Usuń prompt ${index + 1}`}
        disabled={disabled || !canRemove}
        onClick={onRemove}
      >
        Usuń
      </Button>
    </div>
  );
}
