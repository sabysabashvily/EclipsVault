import type { Locator } from '@playwright/test';

/** The computed styles that decide how an element looks, for `visual parity:` comparisons. */
export function lookOf(element: Locator): Promise<string[]> {
  return element.evaluate((node) => {
    const style = getComputedStyle(node);
    return [
      style.color,
      style.backgroundColor,
      style.borderColor,
      style.fontFamily,
      style.fontSize,
      style.fontWeight,
      style.textTransform,
      style.padding,
      style.borderRadius,
      style.boxShadow,
    ];
  });
}
