#!/usr/bin/env bash
# Install git hooks for this repository.
# Run once after cloning: ./scripts/install-hooks.sh
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
HOOKS_DIR="$SCRIPT_DIR/hooks"

if [ ! -d "$HOOKS_DIR" ]; then
  echo "❌ $HOOKS_DIR not found"
  exit 1
fi

# Every executable hook in scripts/hooks/ — README.md and friends excluded.
# Generic on purpose: adding a hook file is the whole install step.
INSTALLED=()
for hook in "$HOOKS_DIR"/*; do
  [ -f "$hook" ] || continue
  case "$(basename "$hook")" in
    *.md | *.txt | .*) continue ;;
  esac
  chmod +x "$hook"
  INSTALLED+=("$(basename "$hook")")
done

if [ ${#INSTALLED[@]} -eq 0 ]; then
  echo "❌ no hook files in $HOOKS_DIR"
  exit 1
fi

# Store the path relative to the working-tree root when we can: core.hooksPath
# lives in the shared config, so an absolute path here would point every linked
# worktree (.agents/worktree/*) at this one checkout.
# `cd … && pwd` on both sides so the two paths use the same flavour — on Git
# Bash `git rev-parse` prints `C:/…` while `pwd` prints `/c/…`.
TOPLEVEL=$(cd "$(cd "$SCRIPT_DIR" && git rev-parse --show-toplevel)" && pwd)
REL_HOOKS="${HOOKS_DIR#"$TOPLEVEL"/}"

git config core.hooksPath "$REL_HOOKS"

echo "✅ Git hooks installed (core.hooksPath=$REL_HOOKS)"
echo "   Hooks: ${INSTALLED[*]}"
echo ""
echo "   Bypass for one commit: git commit --no-verify"
echo "   Reinstall:             $0"
echo "   Uninstall:             git config --unset core.hooksPath"
