#!/bin/bash
RUN=$(curl -s "https://api.github.com/repos/trentgu-git/itf_md_editor/actions/runs?per_page=1" | python3 -c 'import sys,json; print(json.load(sys.stdin)["workflow_runs"][0]["id"])')
echo "Latest run: $RUN"
echo "URL: https://github.com/trentgu-git/itf_md_editor/actions/runs/$RUN"
curl -s "https://api.github.com/repos/trentgu-git/itf_md_editor/actions/runs/$RUN/artifacts" | \
python3 - << 'PY'
import sys, json
d = json.load(sys.stdin)
for a in d["artifacts"]:
    size_mb = a["size_in_bytes"] / (1024*1024)
    print(f"  Artifact: {a['name']}  ({size_mb:.1f} MB)")
    print(f"  Download (login required): https://github.com/trentgu-git/itf_md_editor/actions/runs/{a['workflow_run']['id']}/artifacts/{a['id']}")
PY
