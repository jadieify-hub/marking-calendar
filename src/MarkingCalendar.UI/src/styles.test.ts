import { describe, expect, it } from "vitest";
import stylesheet from "./styles.css?raw";

const lightBlock = stylesheet.match(/:root\s*\{([\s\S]*?)\}/)?.[1] ?? "";
const lightVariables = new Map(
  Array.from(lightBlock.matchAll(/--([\w-]+):\s*(#[0-9a-f]{6})/gi), (match) => [match[1], match[2]] as const),
);

function luminance(color: string): number {
  const channels = color.slice(1).match(/.{2}/g)?.map((channel) => Number.parseInt(channel, 16) / 255) ?? [];
  const linear = channels.map((channel) => channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4);
  return 0.2126 * (linear[0] ?? 0) + 0.7152 * (linear[1] ?? 0) + 0.0722 * (linear[2] ?? 0);
}

function contrast(first: string, second: string): number {
  const [lighter, darker] = [luminance(first), luminance(second)].sort((left, right) => right - left);
  return ((lighter ?? 0) + 0.05) / ((darker ?? 0) + 0.05);
}

describe("light theme", () => {
  it("keeps secondary text readable on its light backgrounds", () => {
    const backgrounds = [lightVariables.get("surface"), lightVariables.get("bg")];
    const secondaryText = [lightVariables.get("text-soft"), lightVariables.get("muted")];

    for (const foreground of secondaryText) {
      for (const background of backgrounds) {
        expect(foreground).toBeDefined();
        expect(background).toBeDefined();
        expect(contrast(foreground!, background!)).toBeGreaterThanOrEqual(4.5);
      }
    }
  });
});
