import type { Locator } from '@playwright/test';

/** The computed styles that decide how an element looks, by name, for `visual parity:` comparisons. */
export function lookOf(element: Locator): Promise<Record<string, string>> {
  return element.evaluate((node) => {
    const style = getComputedStyle(node);
    return {
      color: style.color,
      backgroundColor: style.backgroundColor,
      borderColor: style.borderColor,
      fontFamily: style.fontFamily,
      fontSize: style.fontSize,
      fontWeight: style.fontWeight,
      textTransform: style.textTransform,
      padding: style.padding,
      borderRadius: style.borderRadius,
      boxShadow: style.boxShadow,
    };
  });
}
