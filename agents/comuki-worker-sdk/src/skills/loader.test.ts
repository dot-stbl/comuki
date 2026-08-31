import { afterAll, beforeAll, describe, expect, test } from 'bun:test';
import { mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { listSkills, readSkill } from './loader';

let skillsRoot: string;

beforeAll(async () => {
  skillsRoot = await mkdtemp(join(tmpdir(), 'comuki-skills-'));

  await mkdir(join(skillsRoot, 'git-workflow'));
  await writeFile(
    join(skillsRoot, 'git-workflow', 'SKILL.md'),
    ['---', 'name: git-workflow', 'description: Safe branch and commit flow', '---', '', '## Steps', '', 'Branch first.'].join(
      '\n',
    ),
    'utf8',
  );

  await mkdir(join(skillsRoot, 'deploy'));
  await writeFile(
    join(skillsRoot, 'deploy', 'SKILL.md'),
    ['---', 'name: deploy-runbook', 'description: How to ship a worker image', '---', '', 'Build, tag, push.'].join('\n'),
    'utf8',
  );

  await mkdir(join(skillsRoot, 'broken'));
  await writeFile(join(skillsRoot, 'broken', 'SKILL.md'), 'No frontmatter at all.\n', 'utf8');

  await mkdir(join(skillsRoot, 'empty-dir'));

  await writeFile(join(skillsRoot, 'stray-notes.md'), 'not a skill directory\n', 'utf8');
});

afterAll(async () => {
  await rm(skillsRoot, { recursive: true, force: true });
});

describe('listSkills', () => {
  test('lists only valid skills, sorted by name', async () => {
    const skills = await listSkills(skillsRoot);

    expect(skills.map((skill) => skill.name)).toEqual(['deploy-runbook', 'git-workflow']);
  });

  test('skill docs carry frontmatter, body and dir name', async () => {
    const skills = await listSkills(skillsRoot);
    const gitWorkflow = skills.find((skill) => skill.name === 'git-workflow');

    expect(gitWorkflow?.description).toBe('Safe branch and commit flow');
    expect(gitWorkflow?.body).toContain('## Steps');
    expect(gitWorkflow?.dirName).toBe('git-workflow');
  });
});

describe('readSkill', () => {
  test('reads a single skill by directory name', async () => {
    const skill = await readSkill(skillsRoot, 'deploy');

    expect(skill).not.toBeNull();
    expect(skill?.name).toBe('deploy-runbook');
    expect(skill?.dirName).toBe('deploy');
  });

  test('returns null for a missing directory', async () => {
    expect(await readSkill(skillsRoot, 'does-not-exist')).toBeNull();
  });

  test('returns null for a directory without SKILL.md', async () => {
    expect(await readSkill(skillsRoot, 'empty-dir')).toBeNull();
  });

  test('returns null for a SKILL.md without valid frontmatter', async () => {
    expect(await readSkill(skillsRoot, 'broken')).toBeNull();
  });
});
