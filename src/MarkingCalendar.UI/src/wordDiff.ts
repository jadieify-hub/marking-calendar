export type DiffKind = "equal" | "insert" | "delete";

export interface DiffSegment {
  readonly text: string;
  readonly kind: DiffKind;
}

export interface WordDiffResult {
  readonly highlighted: boolean;
  readonly previous: ReadonlyArray<DiffSegment>;
  readonly current: ReadonlyArray<DiffSegment>;
}

const WORD_LIMIT = 2_000;

export function wordDiff(previous: string, current: string): WordDiffResult {
  const before = words(previous);
  const after = words(current);
  if (before.length > WORD_LIMIT || after.length > WORD_LIMIT) {
    return {
      highlighted: false,
      previous: [{ text: previous, kind: "equal" }],
      current: [{ text: current, kind: "equal" }],
    };
  }
  if (previous === current) {
    return {
      highlighted: true,
      previous: [{ text: previous, kind: "equal" }],
      current: [{ text: current, kind: "equal" }],
    };
  }

  const width = after.length + 1;
  const lcs = new Uint16Array((before.length + 1) * width);
  for (let left = before.length - 1; left >= 0; left--) {
    for (let right = after.length - 1; right >= 0; right--) {
      const index = left * width + right;
      lcs[index] = before[left] === after[right]
        ? lcs[(left + 1) * width + right + 1]! + 1
        : Math.max(lcs[(left + 1) * width + right]!, lcs[left * width + right + 1]!);
    }
  }

  const previousSegments: DiffSegment[] = [];
  const currentSegments: DiffSegment[] = [];
  let left = 0;
  let right = 0;
  while (left < before.length && right < after.length) {
    if (before[left] === after[right]) {
      append(previousSegments, before[left]!, "equal");
      append(currentSegments, after[right]!, "equal");
      left++;
      right++;
    } else if (lcs[(left + 1) * width + right]! >= lcs[left * width + right + 1]!) {
      append(previousSegments, before[left++]!, "delete");
    } else append(currentSegments, after[right++]!, "insert");
  }
  while (left < before.length) append(previousSegments, before[left++]!, "delete");
  while (right < after.length) append(currentSegments, after[right++]!, "insert");
  return { highlighted: true, previous: previousSegments, current: currentSegments };
}

const words = (value: string): string[] => value.trim() ? value.trim().split(/\s+/u) : [];

function append(target: DiffSegment[], word: string, kind: DiffKind): void {
  const previous = target.at(-1);
  if (previous?.kind === kind) target[target.length - 1] = { text: `${previous.text} ${word}`, kind };
  else target.push({ text: word, kind });
}
