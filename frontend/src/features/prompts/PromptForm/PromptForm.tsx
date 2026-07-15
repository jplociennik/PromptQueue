import { useState } from 'react';
import type { FormEvent } from 'react';
import { isBlank, usePromptFields } from '../../../hooks/usePromptFields/usePromptFields';
import { useCreatePrompts } from '../../../hooks/useCreatePrompts/useCreatePrompts';
import { PromptField } from '../PromptField/PromptField';
import { Button } from '../../../components/UI/Button/Button';
import { Alert } from '../../../components/UI/Alert/Alert';
import styles from './PromptForm.module.css';

interface PromptFormProps {
  onSubmitted?: () => void;
}

export function PromptForm({ onSubmitted }: PromptFormProps) {
  const { fields, add, remove, change, reset } = usePromptFields();
  const { state, submit } = useCreatePrompts();
  const [submitAttempted, setSubmitAttempted] = useState(false);
  const isSubmitting = state.status === 'submitting';
  const canRemove = fields.length > 1;

  const handleSubmit = async (event: FormEvent) => {
    event.preventDefault();
    setSubmitAttempted(true);

    if (fields.some((field) => isBlank(field.value))) return;

    const ok = await submit(fields.map((field) => field.value));

    if (ok) {
      reset();
      setSubmitAttempted(false);
      onSubmitted?.();
    }
  };

  const errorDetails =
    state.status === 'error' && state.validationErrors ? Object.values(state.validationErrors).flat() : [];

  return (
    <form className={styles.form} onSubmit={handleSubmit}>
      {fields.map((field, index) => (
        <PromptField
          key={field.id}
          index={index}
          value={field.value}
          invalid={submitAttempted && isBlank(field.value)}
          disabled={isSubmitting}
          canRemove={canRemove}
          onChange={(value) => change(field.id, value)}
          onRemove={() => remove(field.id)}
        />
      ))}
      <Button type="button" disabled={isSubmitting} onClick={add}>
        Dodaj prompt
      </Button>
      <Button type="submit" disabled={isSubmitting}>
        Wyślij
      </Button>
      {state.status === 'success' && <Alert variant="success">Dodano promptów: {state.count}.</Alert>}
      {state.status === 'error' && (
        <Alert variant="error">
          {errorDetails.length > 0 ? (
            <ul className={styles.errors}>
              {errorDetails.map((detail, index) => (
                <li key={index}>{detail}</li>
              ))}
            </ul>
          ) : (
            state.message
          )}
        </Alert>
      )}
    </form>
  );
}
