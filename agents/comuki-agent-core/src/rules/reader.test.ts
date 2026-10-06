import { describe, expect, test } from "bun:test"
import { parseRuleDoc } from "./reader"

describe("parseRuleDoc", () => {
  test("parses frontmatter with name, description and scalar scope", () => {
    const rule = parseRuleDoc(
      [
        "---",
        "name: no-any",
        "description: Forbids the any type",
        "scope: src/**/*.ts",
        "---",
        "",
        "Use unknown instead.",
      ].join("\n")
    )

    expect(rule).toEqual({
      name: "no-any",
      description: "Forbids the any type",
      scope: "src/**/*.ts",
      version: "0.1.0",
      body: "\nUse unknown instead.",
    })
  })

  test("parses scope as a flow list", () => {
    const rule = parseRuleDoc(
      [
        "---",
        "name: multi",
        "description: desc",
        "scope: [src, tests]",
        "---",
        "body",
      ].join("\n")
    )

    expect(rule?.scope).toEqual(["src", "tests"])
  })

  test("parses scope as a block list", () => {
    const rule = parseRuleDoc(
      [
        "---",
        "name: multi",
        "description: desc",
        "scope:",
        "  - src",
        "  - tests",
        "---",
        "body",
      ].join("\n")
    )

    expect(rule?.scope).toEqual(["src", "tests"])
  })

  test("unquotes quoted values and keeps colons inside them", () => {
    const rule = parseRuleDoc(
      [
        "---",
        'name: "quoted name"',
        "description: 'note: with colon'",
        "---",
        "",
      ].join("\n")
    )

    expect(rule?.name).toBe("quoted name")
    expect(rule?.description).toBe("note: with colon")
  })

  test("tolerates CRLF line endings", () => {
    const rule = parseRuleDoc(
      [
        "---",
        "name: crlf",
        "description: windows rule",
        "---",
        "",
        "Body text.",
      ].join("\r\n")
    )

    expect(rule?.name).toBe("crlf")
    expect(rule?.body).toContain("Body text.")
  })

  test("ignores comments and unknown keys", () => {
    const rule = parseRuleDoc(
      [
        "---",
        "# a comment inside frontmatter",
        "name: n",
        "description: d",
        "priority: high",
        "---",
        "",
      ].join("\n")
    )

    expect(rule?.name).toBe("n")
    expect("priority" in (rule ?? {})).toBe(false)
  })

  test("returns null when frontmatter is missing", () => {
    expect(parseRuleDoc("# Just markdown\n\nNo frontmatter here.")).toBeNull()
    expect(parseRuleDoc("")).toBeNull()
  })

  test("returns null when frontmatter is unterminated", () => {
    expect(
      parseRuleDoc("---\nname: broken\ndescription: no closing fence")
    ).toBeNull()
  })

  test("returns null when frontmatter lacks a name", () => {
    expect(
      parseRuleDoc("---\ndescription: no name field\n---\nbody")
    ).toBeNull()
  })

  test("returns null when name is empty", () => {
    expect(parseRuleDoc('---\nname: ""\ndescription: d\n---\nbody')).toBeNull()
  })
})

describe("parseRuleDoc (task 25.1 — skill metadata)", () => {
  test("defaults version to 0.1.0 when absent", () => {
    const rule = parseRuleDoc(
      [
        "---",
        "name: no-version",
        "description: bare skill",
        "---",
        "",
        "body",
      ].join("\n")
    )

    expect(rule?.version).toBe("0.1.0")
  })

  test("parses an explicit version", () => {
    const rule = parseRuleDoc(
      [
        "---",
        "name: versioned",
        "description: explicit version",
        "version: 1.2.0",
        "---",
        "",
        "body",
      ].join("\n")
    )

    expect(rule?.version).toBe("1.2.0")
  })

  test("parses trigger_when as a scalar", () => {
    const rule = parseRuleDoc(
      [
        "---",
        "name: t",
        "description: d",
        "trigger_when: drafting a long-form document",
        "---",
        "",
        "body",
      ].join("\n")
    )

    expect(rule?.triggerWhen).toBe("drafting a long-form document")
  })

  test("parses trigger_when as a flow list", () => {
    const rule = parseRuleDoc(
      [
        "---",
        "name: t",
        "description: d",
        "trigger_when: [a, b, c]",
        "---",
        "",
        "body",
      ].join("\n")
    )

    expect(rule?.triggerWhen).toEqual(["a", "b", "c"])
  })

  test("parses trigger_when as a block list", () => {
    const rule = parseRuleDoc(
      [
        "---",
        "name: t",
        "description: d",
        "trigger_when:",
        "  - a",
        "  - b",
        "---",
        "",
        "body",
      ].join("\n")
    )

    expect(rule?.triggerWhen).toEqual(["a", "b"])
  })

  test("parses validate_against as a list of strings and SourceRef objects", () => {
    const rule = parseRuleDoc(
      [
        "---",
        "name: v",
        "description: d",
        "validate_against:",
        '  - "../../rules/citation-format.md"',
        '  - { kind: knowledge, id: "doc/citation-style@v3" }',
        "---",
        "",
        "body",
      ].join("\n")
    )

    expect(rule?.validateAgainst).toEqual([
      "../../rules/citation-format.md",
      { kind: "knowledge", id: "doc/citation-style@v3" },
    ])
  })

  test("parses validate_against as a single string", () => {
    const rule = parseRuleDoc(
      [
        "---",
        "name: v",
        "description: d",
        "validate_against: ../../rules/citation-format.md",
        "---",
        "",
        "body",
      ].join("\n")
    )

    expect(rule?.validateAgainst).toBe("../../rules/citation-format.md")
  })

  test("parses validate_against as a flow list of strings and objects", () => {
    const rule = parseRuleDoc(
      [
        "---",
        "name: v",
        "description: d",
        'validate_against: ["a.md", { kind: control, id: "rule-x" }]',
        "---",
        "",
        "body",
      ].join("\n")
    )

    expect(rule?.validateAgainst).toEqual([
      "a.md",
      { kind: "control", id: "rule-x" },
    ])
  })

  test("omits trigger_when and validateAgainst when absent", () => {
    const rule = parseRuleDoc(
      ["---", "name: bare", "description: d", "---", "", "body"].join("\n")
    )

    expect(rule?.triggerWhen).toBeUndefined()
    expect(rule?.validateAgainst).toBeUndefined()
    expect(rule?.version).toBe("0.1.0")
  })

  test("carries full metadata end-to-end (spec example)", () => {
    const rule = parseRuleDoc(
      [
        "---",
        "name: citation-cleanup",
        "description: Cleans up inline citations",
        "trigger_when: drafting a long-form document that needs citation cleanup",
        "validate_against:",
        '  - "../../rules/citation-format.md"',
        '  - { kind: knowledge, id: "doc/citation-style@v3" }',
        "version: 1.2.0",
        "---",
        "",
        "body",
      ].join("\n")
    )

    expect(rule).toEqual({
      name: "citation-cleanup",
      description: "Cleans up inline citations",
      triggerWhen: "drafting a long-form document that needs citation cleanup",
      validateAgainst: [
        "../../rules/citation-format.md",
        { kind: "knowledge", id: "doc/citation-style@v3" },
      ],
      version: "1.2.0",
      body: "\nbody",
    })
  })
})
