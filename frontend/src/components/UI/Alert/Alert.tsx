import type { ReactNode } from 'react';
import styles from './Alert.module.css';

interface AlertProps {
  variant: 'success' | 'error';
  children: ReactNode;
}

export function Alert({ variant, children }: AlertProps) {
  const classes = [styles.alert, styles[variant]].filter(Boolean).join(' ');
  return (
    <div role="alert" className={classes}>
      {children}
    </div>
  );
}
