/** Advisory guessing cost; the server enforces length and breach checks. */
export function passwordStrength(value: string): { score: number; text: string } {
  let alphabet = 0;
  if (/[a-z]/.test(value)) alphabet += 26;
  if (/[A-Z]/.test(value)) alphabet += 26;
  if (/[0-9]/.test(value)) alphabet += 10;
  if (/[^A-Za-z0-9]/.test(value)) alphabet += 33;
  let bits = value.length * Math.log2(Math.max(alphabet, 2));
  const distinct = new Set(value).size;
  if (distinct <= 2) bits *= 0.35;
  else if (distinct / value.length < 0.4) bits *= 0.7;
  if (/^(?:0123456789|abcdefghij|qwertyuiop)/i.test(value)) bits *= 0.4;
  if (value.length < 12)
    return {
      score: 0,
      text: `Too short — ${12 - value.length} more character${value.length === 11 ? '' : 's'} needed`,
    };
  if (bits < 60) return { score: 1, text: 'Weak — predictable for its length' };
  if (bits < 80) return { score: 2, text: 'Fair' };
  if (bits < 110) return { score: 3, text: 'Strong' };
  return { score: 4, text: 'Very strong' };
}
