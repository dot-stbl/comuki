import { describe, expect, test } from 'bun:test';
import { parseReport, reportSchema } from './report';

describe('parseReport', () => {
  test('parses a succeeded report with artifacts', () => {
    const report = parseReport({
      status: 'succeeded',
      summary: 'Added the login page behind /login',
      artifacts: ['src/pages/login.tsx', 'docs/login.md'],
    });

    expect(report.status).toBe('succeeded');
    expect(report.artifacts).toEqual(['src/pages/login.tsx', 'docs/login.md']);
  });

  test('parses a minimal failed report', () => {
    const report = parseReport({ status: 'failed', summary: 'Build failed: type error' });

    expect(report).toEqual({ status: 'failed', summary: 'Build failed: type error' });
    expect(report.artifacts).toBeUndefined();
  });

  test('rejects an unknown status', () => {
    expect(() => parseReport({ status: 'running', summary: 'still going' })).toThrow();
  });

  test('rejects a missing summary', () => {
    expect(() => parseReport({ status: 'cancelled' })).toThrow();
  });

  test('schema round-trips a valid report', () => {
    const report = parseReport({ status: 'cancelled', summary: 'Stopped by orchestrator' });

    expect(reportSchema.parse(JSON.parse(JSON.stringify(report)))).toEqual(report);
  });
});
