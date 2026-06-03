#!/bin/bash
RUN_ID=$(curl -s "https://api.github.com/repos/trentgu-git/itf_md_editor/actions/runs?per_page=1" | python3 -c 'import sys,json; print(json.load(sys.stdin)["workflow_runs"][0]["id"])')
JOB_ID=$(curl -s "https://api.github.com/repos/trentgu-git/itf_md_editor/actions/runs/$RUN_ID/jobs" | python3 -c 'import sys,json; print(json.load(sys.stdin)["jobs"][0]["id"])')
echo "Run: $RUN_ID  Job: $JOB_ID"
echo "Logs (last 100 lines, errors only):"
curl -sL "https://api.github.com/repos/trentgu-git/itf_md_editor/actions/jobs/$JOB_ID/logs" 2>&1 | grep -E "error|Error|CSC|CS[0-9]{4}" | head -40
