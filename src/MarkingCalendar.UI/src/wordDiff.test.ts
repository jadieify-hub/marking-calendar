import { describe, expect, it } from "vitest";
import { wordDiff } from "./wordDiff";

describe("wordDiff", () => {
  it("marks inserted words", () => {
    expect(wordDiff("обязательная маркировка", "обязательная цифровая маркировка")).toEqual({
      highlighted: true,
      previous: [{ text: "обязательная маркировка", kind: "equal" }],
      current: [
        { text: "обязательная", kind: "equal" },
        { text: "цифровая", kind: "insert" },
        { text: "маркировка", kind: "equal" },
      ],
    });
  });

  it("marks deleted words", () => {
    expect(wordDiff("обязательная цифровая маркировка", "обязательная маркировка").previous).toEqual([
      { text: "обязательная", kind: "equal" },
      { text: "цифровая", kind: "delete" },
      { text: "маркировка", kind: "equal" },
    ]);
  });

  it("represents a replacement as deletion and insertion", () => {
    const result = wordDiff("начало оборота", "старт оборота");
    expect(result.previous).toEqual([{ text: "начало", kind: "delete" }, { text: "оборота", kind: "equal" }]);
    expect(result.current).toEqual([{ text: "старт", kind: "insert" }, { text: "оборота", kind: "equal" }]);
  });

  it("keeps identical strings as one unchanged segment", () => {
    expect(wordDiff("без изменений", "без изменений")).toEqual({
      highlighted: true,
      previous: [{ text: "без изменений", kind: "equal" }],
      current: [{ text: "без изменений", kind: "equal" }],
    });
  });

  it("falls back to plain text above the word limit", () => {
    const longText = Array.from({ length: 2_001 }, () => "слово").join(" ");
    expect(wordDiff(longText, "коротко")).toEqual({
      highlighted: false,
      previous: [{ text: longText, kind: "equal" }],
      current: [{ text: "коротко", kind: "equal" }],
    });
  });
});
