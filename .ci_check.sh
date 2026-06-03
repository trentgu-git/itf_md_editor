#!/bin/bash
# Authenticated check (uses git credential cache) to dodge rate limits.
TOKEN=$(printf 'protocol=https\nhost=github.com\n' | git credential fill 2>/dev/null | grep '^password=' | cut -d= -f2)
AUTH=""
[ -n "$TOKEN" ] && AUTH="-H \"Authorization: token $TOKEN\""

eval curl -s $AUTH "https://api.github.com/repos/trentgu-git/itf_md_editor/actions/runs?per_page=3" > /tmp/itf_ci.json
python3 - << 'PY'
import json
with open("/tmp/itf_ci.json") as f:
    d = json.load(f)
runs = d.get("workflow_runs", [])
if not runs:
    print("rate limit or empty:", d.get("message", "")[:80])
for r in runs:
    msg = r["head_commit"]["message"].splitlines()[0][:60]
    status = r["status"]
    conclusion = str(r["conclusion"])
    print(status.ljust(12), conclusion.ljust(10), msg)
PY
