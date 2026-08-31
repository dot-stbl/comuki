import { readdir, readFile } from 'node:fs/promises';
import { join } from 'node:path';
import { parseRuleDoc } from '@comuki/agent-core';

/**
 * Loader for skill directories in the control-plane layout:
 * `<skills-root>/<skill-name>/SKILL.md`, where each SKILL.md is markdown with
 * a `name` / `description` frontmatter (the same document format as rule
 * docs, parsed by agent-core). Skills without valid frontmatter are skipped,
 * not fatal — one broken skill must not hide the rest of the catalog.
 */

export interface SkillDoc {
  readonly name: string;
  readonly description: string;
  readonly body: string;
  readonly dirName: string;
}

/** Lists every valid skill directly under `skillsDir`, sorted by name. */
export async function listSkills(skillsDir: string): Promise<SkillDoc[]> {
  const entries = await readdir(skillsDir, { withFileTypes: true });
  const skills: SkillDoc[] = [];

  for (const entry of entries) {
    if (!entry.isDirectory()) {
      continue;
    }
    const skill = await readSkill(skillsDir, entry.name);
    if (skill !== null) {
      skills.push(skill);
    }
  }

  return skills.sort((left, right) => left.name.localeCompare(right.name));
}

/** Reads a single skill by its directory name. Returns `null` when the skill or its frontmatter is missing. */
export async function readSkill(skillsDir: string, dirName: string): Promise<SkillDoc | null> {
  let text: string;
  try {
    text = await readFile(join(skillsDir, dirName, 'SKILL.md'), 'utf8');
  } catch {
    return null;
  }

  const rule = parseRuleDoc(text);
  if (rule === null) {
    return null;
  }

  return { name: rule.name, description: rule.description, body: rule.body, dirName };
}
