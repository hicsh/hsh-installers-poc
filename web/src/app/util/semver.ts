/** Dotted-numeric "less than" — enough for x.y.z agent versions, no library needed. */
export function semverLt(a: string, b: string): boolean {
  const pa = a.split('.').map(Number);
  const pb = b.split('.').map(Number);
  for (let i = 0; i < Math.max(pa.length, pb.length); i++) {
    const na = pa[i] ?? 0;
    const nb = pb[i] ?? 0;
    if (na !== nb) return na < nb;
  }
  return false;
}

/** Bumps a version for the ?demoUpdate flow (no real release needed). */
export function bumpVersion(v: string, part: 'major' | 'patch'): string {
  const [major, minor, patch] = v.split('.').map((n) => Number(n) || 0);
  return part === 'major' ? `${major + 1}.0.0` : `${major}.${minor}.${patch + 1}`;
}
