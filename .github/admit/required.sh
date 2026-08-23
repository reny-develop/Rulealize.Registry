#!/usr/bin/env bash
#
# Says whether main still requires the checks a submission is admitted on.
#
# gate.sh decides that a pull request is a submission, and admit.yml turns on auto-merge —
# which merges the moment the *required* checks pass. Which checks those are is a repository
# setting rather than a file in this repository, so a submission would merge without the job
# that fetches its package ever having run, and nothing here would say so.
#
# This is what says so. It runs where the decision is made, against the rules the merge will
# actually be held to, and admit.yml turns auto-merge on for nothing this refuses.
#
#   required.sh <required.json> <rules.json>
#
# <rules.json> is `gh api repos/<owner>/<repo>/rules/branches/main`: every active rule on the
# branch, whichever ruleset it came from. Reading it asks for no more than read access.
#
# Exit 0 agrees and prints nothing. Exit 1 prints what is not required, as markdown. Exit 2 is
# a usage error. A branch that requires more than this list agrees: this says what has to be
# there, not what may be.

set -uo pipefail

if ! command -v jq >/dev/null; then
    echo "required.sh needs jq." >&2
    exit 2
fi

if [[ $# -ne 2 ]]; then
    echo "usage: required.sh <required.json> <rules.json>" >&2
    exit 2
fi

required=$1
rules=$2

for file in "$required" "$rules"; do
    if [[ ! -s "$file" ]] || ! jq -e . "$file" >/dev/null 2>&1; then
        echo "'${file}' is missing or is not valid JSON." >&2
        exit 2
    fi
done

if ! jq -e '.checks | type == "array"' "$required" >/dev/null 2>&1; then
    echo "'${required}' does not list the checks a submission is admitted on." >&2
    exit 2
fi

# Anything other than a list of rules is the branch answering something else — an error body,
# or an endpoint that moved. It is not evidence that a check is required, so it is refused
# rather than read for the part that happens to parse.
if ! jq -e 'type == "array"' "$rules" >/dev/null 2>&1; then
    echo "\`main\` did not answer with a list of rules, so what it requires is not known here."
    exit 1
fi

contexts=$(jq -c '[ .[]
    | select(.type == "required_status_checks")
    | .parameters.required_status_checks[]?.context ]' "$rules")

if [[ "$(jq 'length' <<<"$contexts")" -eq 0 ]]; then
    echo "\`main\` requires no status check, so auto-merge would merge a submission before its package had been fetched."
    exit 1
fi

missing=$(jq -r --argjson required "$contexts" '.checks - $required | .[]' "$required")

if [[ -z "$missing" ]]; then
    exit 0
fi

while IFS= read -r check; do
    [[ -z "$check" ]] && continue
    echo "\`${check}\` is not a required status check on \`main\`."
done <<<"$missing"

exit 1
