export function safeExternalUrl(value: string | null | undefined): string | null {
  if (!value) return null;
  try {
    const url = new URL(value);
    return url.protocol === 'https:' || url.protocol === 'http:' ? url.href : null;
  } catch { return null; }
}

export function measurement(min: number | null | undefined, max: number | null | undefined, unit: string, factor = 1): string {
  const low = min == null ? null : Math.round(min * factor);
  const high = max == null ? null : Math.round(max * factor);
  if (low === null && high === null) return 'Not recorded';
  if (low === null) return `Up to ${high} ${unit}`;
  if (high === null) return `From ${low} ${unit}`;
  return `${low === high ? low : `${low}–${high}`} ${unit}`;
}
