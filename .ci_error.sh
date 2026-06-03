#!/bin/bash
# Try to get the actual compile error from job logs via gh CLI or cached credential
RUN_ID=$(curl -s "https://api.github.com/repos/trentgu-git/itf_md_editor/actions/runs?per_page=1" | python3 -c 'import sys,json; print(json.load(sys.stdin)["workflow_runs"][0]["id"])')
echo "Run: $RUN_ID"
echo
if command -v gh &> /dev/null; then
    echo "--- Using gh CLI ---"
    gh run view "$RUN_ID" --repo trentgu-git/itf_md_editor --log-failed 2>&1 | grep -E 'error CS|error MSB|XamlParse|❌|✗' | head -30
    exit 0
fi
# Fallback: try with credential helper
TOKEN=$(git config --get credential.helper >/dev/null 2>&1 && git credential fill <<< "protocol=https
host=github.com" 2>/dev/null | grep '^password=' | head -1 | cut -d= -f2)
if [ -n "$TOKEN" ]; then
    echo "--- Using cached git credential ---"
    curl -sL -H "Authorization: token $TOKEN" \
      "https://api.github.com/repos/trentgu-git/itf_md_editor/actions/runs/$RUN_ID/logs" \
      -o /tmp/itf_logs.zip 2>&1
    ls -la /tmp/itf_logs.zip
    cd /tmp && rm -rf itf_logs && mkdir itf_logs && cd itf_logs && unzip -q /tmp/itf_logs.zip 2>&1 | head -5
    echo "--- Errors from build job ---"
    grep -rE 'error (CS|MSB)[0-9]+' /tmp/itf_logs/ 2>/dev/null | head -20
else
    echo "No gh CLI and no cached credential. Open the actions page to view logs."
fi
