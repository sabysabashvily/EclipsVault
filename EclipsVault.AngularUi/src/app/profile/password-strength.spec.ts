import { passwordStrength } from './password-strength';

describe('passwordStrength', () => {
  it.each([
    ['short', 0, 'Too short — 7 more characters needed'],
    ['aaaaaaaaaaaa', 1, 'Weak — predictable for its length'],
    ['abcdefghijklm', 1, 'Weak — predictable for its length'],
    ['Different-Password-456!', 4, 'Very strong'],
  ])('estimates %s without adding a composition rule', (password, score, text) => {
    expect(passwordStrength(password)).toEqual({ score, text });
  });
});
