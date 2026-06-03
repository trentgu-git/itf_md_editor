#!/bin/bash
RUN_ID=$(curl -s "https://api.github.com/repos/trentgu-git/itf_md_editor/actions/runs?per_page=1" | python3 -c 'import sys,json; print(json.load(sys.stdin)["workflow_runs"][0]["id"])')
echo "Run: $RUN_ID"
# Get check runs (each step's annotations are public)
curl -s "https://api.github.com/repos/trentgu-git/itf_md_editor/check-runs?check_name=build&filter=latest" | python3 - << 'PY'
import sys, json
try:
    d = json.load(sys.stdin)
    print(json.dumps(d, indent=2)[:3000])
except Exception as e:
    print(f"parse error: {e}")
PY
echo "---"
# Try annotations via check runs in the latest run
curl -s "https://api.github.com/repos/trentgu-git/itf_md_editor/actions/runs/$RUN_ID/jobs" | python3 - << 'PY'
import sys, json
d = json.load(sys.stdin)
for j in d.get("jobs", []):
    print(f"\nJob: {j['name']}  conclusion={j['conclusion']}")
    for s in j.get("steps", []):
        if s["conclusion"] in ("failure", "cancelled"):
            print(f"  ✗ Step: {s['name']}  ({s['conclusion']})")
PY
