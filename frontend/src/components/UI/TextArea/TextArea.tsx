import type { TextareaHTMLAttributes } from 'react';
import styles from './TextArea.module.css';

interface TextAreaProps extends TextareaHTMLAttributes<HTMLTextAreaElement> {
  invalid?: boolean;
}

export function TextArea({ invalid, className, ...rest }: TextAreaProps) {
  const classes = [styles.textarea, invalid && styles.invalid, className].filter(Boolean).join(' ');
  return <textarea className={classes} aria-invalid={invalid} {...rest} />;
}
