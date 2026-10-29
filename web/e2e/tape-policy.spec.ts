import { test, expect } from '@playwright/test';
import { validateSelection, validateDuration, FoundFile } from '../src/app/core/tape-policy';

function file(size = 1, folders: string[] = [], name = 'Tape.mp4'): FoundFile {
  return { file: { name, size, type: 'video/mp4' } as File, folders };
}

test('selection boundaries reject whole selections without reading their bytes', () => {
  expect(validateSelection(Array.from({ length: 5 }, () => file()))).toHaveLength(5);
  expect(() => validateSelection(Array.from({ length: 6 }, () => file()))).toThrow('up to 5');
  expect(validateSelection([file(500_000_000), file(500_000_000)])).toHaveLength(2);
  expect(() => validateSelection([file(500_000_001)])).toThrow('500 MB');
  expect(() => validateSelection([file(500_000_000), file(500_000_000), file()])).toThrow('1 GB');
  expect(validateSelection([file(1, ['甲']), file(1, ['乙']), file()])).toHaveLength(3);
  expect(() => validateSelection([file(1, ['a']), file(1, ['b']), file(1, ['c']), file()])).toThrow('3 channels');
  expect(() => validateSelection([file(1, Array(9).fill('nested'))])).toThrow('scan limit');
  expect(() => validateSelection(Array.from({ length: 1001 }, () => file()))).toThrow('scan limit');
});

test('duration accepts exactly 30 minutes and rejects unknown and oversized media', () => {
  expect(() => validateDuration(1800)).not.toThrow();
  for (const duration of [1800.01, Infinity, NaN, 0, -1])
    expect(() => validateDuration(duration)).toThrow('30 minutes');
});
