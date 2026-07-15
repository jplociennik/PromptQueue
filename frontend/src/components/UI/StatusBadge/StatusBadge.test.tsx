import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { StatusBadge } from './StatusBadge';
import type { PromptStatus } from '../../../api/types';

afterEach(cleanup);

const cases: [PromptStatus, string][] = [
  ['pending', 'Oczekuje'],
  ['processing', 'Przetwarzanie'],
  ['completed', 'Zakończono'],
  ['failed', 'Błąd'],
];

describe('StatusBadge', () => {
  it.each(cases)('renders the "%s" status with its Polish label', (status, label) => {
    render(<StatusBadge status={status} />);

    expect(screen.getByText(label)).toBeInTheDocument();
  });
});
