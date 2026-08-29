#!/usr/bin/env bash
# Install git hooks for this repository.
# Run once after cloning: ./scripts/install-hooks.sh
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
HOOKS_DIR="$SCRIPT_DIR/hooks"
HOOK_FILE="$HOOKS_DIR/pre-commit"

if [ ! -f "$HOOK_FILE" ]; then
  echo "❌ $HOOK_FILE not found"
  exit 1
fi

chmod +x "$HOOK_FILE"
git config core.hooksPath "$HOOKS_DIR"

REL_HOOKS=$(realpath --relative-to="$(pwd)" "$HOOKS_DIR" 2>/dev/null || echo "$HOOKS_DIR")
echo "✅ Git hooks installed (core.hooksPath=$REL_HOOKS)"
echo ""
echo "   Bypass for one commit: git commit --no-verify"
echo "   Reinstall:             $0"
echo "   Uninstall:             git config --unset core.hooksPath"
