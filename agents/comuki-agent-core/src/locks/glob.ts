/**
 * Minimal glob → RegExp compiler for lock patterns. Supports `*` (one path
 * segment), `**` (any number of segments; a trailing double-star before a
 * slash also matches zero directories) and `?` (one character within a
 * segment). With `segmentStars: false`, `*` and `?` cross `/` — used for
 * tool-call and git-ref patterns where `/` is an ordinary character.
 */
export interface GlobOptions {
  readonly segmentStars?: boolean;
}

export function globToRegExp(pattern: string, options: GlobOptions = {}): RegExp {
  const segmentStars = options.segmentStars ?? true;
  let source = '';
  let index = 0;

  while (index < pattern.length) {
    const char = pattern.charAt(index);

    if (char === '*') {
      let stars = 1;
      while (pattern.charAt(index + stars) === '*') {
        stars++;
      }
      index += stars;

      if (stars >= 2) {
        if (pattern.charAt(index) === '/') {
          source += '(?:.*/)?';
          index++;
        } else {
          source += '.*';
        }
      } else {
        source += segmentStars ? '[^/]*' : '.*';
      }
      continue;
    }

    if (char === '?') {
      source += segmentStars ? '[^/]' : '.';
      index++;
      continue;
    }

    source += escapeRegExp(char);
    index++;
  }

  return new RegExp(`^${source}$`);
}

function escapeRegExp(char: string): string {
  return /[.*+?^${}()|[\]\\]/.test(char) ? `\\${char}` : char;
}
