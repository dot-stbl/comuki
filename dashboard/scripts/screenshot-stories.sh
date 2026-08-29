#!/bin/bash
# Screenshot all Storybook stories via native Chrome --screenshot mode (no CDP/playwright).
# Output: dashboard/.audit/screenshots/<story-id>.png
# Usage: bash scripts/screenshot-stories.sh [parallel_jobs]

set -e

CHROME="C:/Users/bradw/AppData/Local/ms-playwright/chromium-1223/chrome-win64/chrome.exe"
STORYBOOK_URL="http://localhost:6006"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
OUTPUT_DIR="$SCRIPT_DIR/../.audit/screenshots"
PARALLEL="${1:-4}"

if [ ! -f "$CHROME" ]; then
  echo "ERROR: Chrome not found at $CHROME"
  exit 1
fi

mkdir -p "$OUTPUT_DIR"

# Clean previous screenshots (keep test-button.png as sanity)
echo "Cleaning previous screenshots..."
find "$OUTPUT_DIR" -name "*.png" -not -name "test-button.png" -delete 2>/dev/null || true

# Get all story IDs (type=story, not docs) from Storybook index.json
echo "Fetching story index from $STORYBOOK_URL/index.json..."
STORY_IDS=$(curl -s "$STORYBOOK_URL/index.json" | node -e "
  let data = '';
  process.stdin.on('data', c => data += c);
  process.stdin.on('end', () => {
    const json = JSON.parse(data);
    Object.values(json.entries)
      .filter(e => e.type === 'story')
      .map(e => e.id)
      .forEach(id => console.log(id));
  });
")

TOTAL=$(echo "$STORY_IDS" | wc -l | tr -d ' ')
echo "Found $TOTAL stories"
echo "Taking screenshots with $PARALLEL parallel jobs..."

# Function to screenshot one story
screenshot_one() {
  local id="$1"
  # Replace / with _ in id for safe filename
  local safe_name="${id//\//_}"
  local out="$OUTPUT_DIR/$safe_name.png"
  "$CHROME" \
    --headless --disable-gpu --no-sandbox --hide-scrollbars \
    --window-size=1440,900 \
    --virtual-time-budget=8000 \
    --screenshot="$out" \
    "$STORYBOOK_URL/?path=/story/$id" >/dev/null 2>&1
  if [ -f "$out" ]; then
    echo "  ✓ $id"
  else
    echo "  ✗ $id (failed)"
  fi
}
export -f screenshot_one
export CHROME STORYBOOK_URL OUTPUT_DIR

# Batched parallel via background processes + wait
i=0
batch_count=0
for id in $STORY_IDS; do
  screenshot_one "$id" &
  i=$((i+1))
  if [ $((i % PARALLEL)) -eq 0 ]; then
    wait
  fi
done
wait

# Count results
COUNT=$(find "$OUTPUT_DIR" -name "*.png" -not -name "test-button.png" 2>/dev/null | wc -l | tr -d ' ')
echo ""
echo "Done: $COUNT/$TOTAL screenshots saved to $OUTPUT_DIR"
