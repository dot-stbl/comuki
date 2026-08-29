#!/usr/bin/env bash
#
# Build the worker image and run pi in headless mode with a trivial prompt,
# then assert that the stream-json output contains at least one event line.
#
# Slice 0 step 0: the cheapest way to confirm that pi runs as a headless
# worker inside a container and emits a parseable stream-json feed. If this
# fails, the rest of Slice 0 has no foundation.
#
# Usage:
#   cd deploy
#   cp .env.example .env       # if not done yet
#   # fill ANTHROPIC_API_KEY (and optionally ANTHROPIC_BASE_URL) in .env
#   bash scripts/test-pi-headless.sh
#
# Exit code:
#   0 — at least one stream-json event line observed
#   1 — build failed, run failed, or no event lines in output

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEPLOY_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
REPO_ROOT="$(cd "${DEPLOY_DIR}/.." && pwd)"

cd "${DEPLOY_DIR}"

if [[ ! -f .env ]]; then
    echo "ERROR: deploy/.env not found. Copy .env.example to .env and fill in ANTHROPIC_API_KEY." >&2
    exit 1
fi

# shellcheck disable=SC1091
source .env

if [[ -z "${ANTHROPIC_API_KEY:-}" ]]; then
    echo "ERROR: ANTHROPIC_API_KEY is empty in deploy/.env." >&2
    echo "       Slice 0 step 0 needs a real key to validate end-to-end stream-json output." >&2
    exit 1
fi

PROMPT="${PI_TEST_PROMPT:-Say hello in exactly one word}"
CONTAINER_CMD=(pi -p "${PROMPT}" --output-format stream-json)

echo "▶ Building worker image (comuki/worker:dev) ..."
podman compose --env-file .env --profile worker build worker

echo "▶ Running pi in headless mode:"
echo "    prompt: ${PROMPT}"
echo "    command: ${CONTAINER_CMD[*]}"

# capture stream-json output; tolerate non-zero exit so we can still inspect stdout
set +e
OUTPUT="$(podman compose --env-file .env --profile worker run --rm worker "${CONTAINER_CMD[@]}" 2>&1)"
RUN_EXIT=$?
set -e

echo "${OUTPUT}" | sed 's/^/    /'

if [[ ${RUN_EXIT} -ne 0 ]]; then
    echo "✗ pi exited non-zero (${RUN_EXIT})" >&2
    exit 1
fi

# A valid stream-json line is a JSON object that starts with `{` and contains
# a `"type"` field somewhere on the line.
if echo "${OUTPUT}" | grep -qE '^\{[[:space:]]*"type"[[:space:]]*:'; then
    echo "✓ pi produced stream-json events (at least one matching '^\{\"type\":')"
    exit 0
fi

echo "✗ no stream-json event lines matched '^\{\"type\":'" >&2
echo "  expected: pi in headless mode should emit one JSON object per line." >&2
echo "  check that pi is installed in the image and ANTHROPIC_API_KEY is valid." >&2
exit 1
