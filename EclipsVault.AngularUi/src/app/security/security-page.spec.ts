import { fixLabels, fixUrls, grades, ringDasharray } from './security-page';

describe('security checkup presentation', () => {
  it('sends each remediation to its Legacy self-service page, or nowhere', () => {
    expect(fixUrls).toEqual({ None: null, Profile: '/Profile', BackupCodes: '/Profile', SignedInDevices: '/Sessions' });
  });

  it('names the fix for each control', () => {
    expect(fixLabels).toEqual({
      TwoStepSignIn: 'Set up two-step', BackupCodes: 'Generate codes',
      Passkey: 'Add a passkey', SignedInDevices: 'Review devices',
    });
  });

  it('labels and colours each grade as the Legacy page does', () => {
    expect(grades).toEqual({
      Strong: { label: 'Strong', class: 'grade-strong' }, Good: { label: 'Good', class: 'grade-good' },
      Fair: { label: 'Fair', class: 'grade-fair' }, AtRisk: { label: 'At risk', class: 'grade-atrisk' },
    });
  });

  it.each([
    [-1, '0.0 326.7'], [0, '0.0 326.7'], [25, '81.7 245.0'], [50, '163.4 163.4'],
    [70, '228.7 98.0'], [90, '294.1 32.7'], [100, '326.7 0.0'], [101, '326.7 0.0'],
  ])('draws score %i on a radius-52 ring with clamped arc and gap to one decimal', (score, expected) => {
    expect(ringDasharray(score)).toBe(expected);
  });
});
