#!/usr/bin/env bash
#
# Writes the ledger a submission would make: the ledger as it is, with one line added in the
# place identifier order puts it.
#
# The whole file is written out again rather than edited, in the shape a person reads it in —
# one entry per line — so the change a submission makes is one added line and, when it sorts
# last, the comma on the line above. Every other line comes out exactly as it went in; the
# cases in test/run.sh hold the ledger on main to that.
#
#   insert.sh <submitted.json> <parsed submission>   (the output of parse.sh)
#   insert.sh <submitted.json>                       (the ledger as it is, reformatted)
#
# Prints the new ledger. Exit 2 is a usage error; anything else wrong with the result is
# gate.sh's to say.

set -euo pipefail

if ! command -v jq >/dev/null; then
    echo "insert.sh needs jq." >&2
    exit 2
fi

if [[ $# -lt 1 || $# -gt 2 ]]; then
    echo "usage: insert.sh <submitted.json> [<parsed submission>]" >&2
    exit 2
fi

parsed=null
if [[ $# -eq 2 ]]; then
    parsed=$(cat "$2")
fi

jq -r --argjson p "$parsed" '
    def line: "{ " + (to_entries | map((.key | tojson) + ": " + (.value | tojson)) | join(", ")) + " }";
    def list($name):
        "  " + ($name | tojson) + ": ["
        + (if (.[$name] | length) == 0 then "]"
           else "\n" + (.[$name] | map("    " + line) | join(",\n")) + "\n  ]" end);

    (if $p == null then .
       else .[$p.list] = ((.[$p.list] // []) + [$p.entry] | sort_by(.id)) end)
    | "{\n  \"$schema\": " + (."$schema" | tojson) + ",\n"
      + list("plugins") + ",\n"
      + list("ruleSets") + "\n}"
' "$1"
