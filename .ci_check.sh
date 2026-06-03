#!/bin/bash
curl -s "https://api.github.com/repos/trentgu-git/itf_md_editor/actions/runs?per_page=3" > /tmp/itf_ci.json
python3 - << 'PY'
import json
with open("/tmp/itf_ci.json") as f:
    d = json.load(f)
for r in d["workflow_runs"]:
    msg = r["head_commit"]["message"].splitlines()[0][:60]
    status = r["status"]
    conclusion = str(r["conclusion"])
    print(status.ljust(12), conclusion.ljust(10), msg)
PY
