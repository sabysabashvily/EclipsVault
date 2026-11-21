import { context, fixLabels, fixUrls, grades, verdict } from './security-page';

describe('security checkup presentation', () => {
  it('sends each remediation to its Legacy self-service page, or nowhere', () => {
    expect(fixUrls).toEqual({
      None: null,
      Profile: '/Profile',
      BackupCodes: '/Profile',
      SignedInDevices: '/Sessions',
    });
  });

  it('names the fix for each control', () => {
    expect(fixLabels).toEqual({
      TwoStepSignIn: 'Set up two-step',
      BackupCodes: 'Generate codes',
      Passkey: 'Add a passkey',
      SignedInDevices: 'Review devices',
    });
  });

  it('labels and tones each grade', () => {
    expect(grades).toEqual({
      Strong: { label: 'Strong', tone: 'ok' },
      Good: { label: 'Good', tone: 'ok' },
      Fair: { label: 'Fair', tone: 'warn' },
      AtRisk: { label: 'At risk', tone: 'danger' },
    });
  });

  it.each([
    [1, 3, { label: 'Action needed', tone: 'danger' }],
    [0, 2, { label: 'Recommended', tone: 'warn' }],
    [0, 0, { label: 'All good', tone: 'ok' }],
  ])(
    'with %i action needed and %i recommended, the verdict is %o',
    (action, recommended, expected) => {
      expect(verdict(action, recommended)).toEqual(expected);
    },
  );

  it.each([
    [0, 4, null, 'All 4 checks pass. Everything here is about your account only.'],
    [1, 3, 'Add a passkey.', '1 check needs you · next: Add a passkey.'],
    [2, 2, 'Generate codes.', '2 checks need you · next: Generate codes.'],
    [2, 2, null, '2 checks need you.'],
  ])('with %i open and %i passing checks, the line reads as expected', (open, pass, next, line) => {
    expect(context(open, pass, next)).toBe(line);
  });
});
