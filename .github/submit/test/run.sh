#!/usr/bin/env bash
#
# The cases for reading a submission out of its issue and writing the ledger it would make.
#
# parse.sh reads what a person wrote, so the cases are the ways a person writes it: the form as
# the site fills it in, a field left empty, a value in backticks, a line ending from Windows.
# insert.sh writes the file a person reads, so its first case is the ledger on main going
# through it unchanged. The last ones run the three scripts in the order the workflow does.
#
#   .github/submit/test/run.sh

set -uo pipefail

if ! command -v jq >/dev/null; then
    echo "These cases need jq, and every one of them would report a failure without it." >&2
    exit 2
fi

here=$(cd "$(dirname "$0")" && pwd)
root=$(cd "$here/../../.." && pwd)
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

passed=0
failed=0

check() {
    local name=$1 ok=$2 detail=${3:-}
    if [[ "$ok" -eq 0 ]]; then
        printf '  ok    %s\n' "$name"
        passed=$((passed + 1))
    else
        printf '  FAIL  %s\n' "$name"
        [[ -n "$detail" ]] && sed 's/^/          /' <<<"$detail"
        failed=$((failed + 1))
    fi
}

# body <kind> <package> <version> [namespace] [shorthand] — an issue body as the form writes it
body() {
    printf '### Kind\n\n%s\n\n### Package identifier\n\n%s\n\n### Version\n\n%s\n\n### Namespace\n\n%s\n\n### Shorthand character\n\n%s\n' \
        "$1" "${2:-_No response_}" "${3:-_No response_}" "${4:-_No response_}" "${5:-_No response_}"
}

# parse <name> <expected json|refuses> [reason] — the body on standard input
parse() {
    local name=$1 expect=$2 reason=${3:-}
    cat > "$work/body.md"
    local output status
    output=$(bash "$here/../parse.sh" "$work/body.md")
    status=$?

    if [[ "$expect" == refuses ]]; then
        [[ $status -eq 1 && "$output" == *"$reason"* ]]
        check "$name" $? "$output"
    else
        [[ $status -eq 0 ]] && jq -e --argjson want "$expect" '. == $want' <<<"$output" >/dev/null
        check "$name" $? "$output"
    fi
}

echo "parse:"

parse reads-a-plugin '{"list":"plugins","entry":{"id":"Acme.Plugin.Dice","version":"1.0.0","namespace":"dice","prefix":null}}' \
    <<<"$(body Plugin Acme.Plugin.Dice 1.0.0 dice)"

parse reads-a-shorthand '{"list":"plugins","entry":{"id":"Acme.Plugin.Dice","version":"1.0.0","namespace":"dice","prefix":"!"}}' \
    <<<"$(body Plugin Acme.Plugin.Dice 1.0.0 dice '!')"

# The site writes "(none)" for a plugin that reserves nothing, and a person might write "none".
parse reads-none-as-no-shorthand '{"list":"plugins","entry":{"id":"Acme.Plugin.Dice","version":"1.0.0","namespace":"dice","prefix":null}}' \
    <<<"$(body Plugin Acme.Plugin.Dice 1.0.0 dice '(none)')"

parse reads-a-backticked-value '{"list":"plugins","entry":{"id":"Acme.Plugin.Dice","version":"1.0.0","namespace":"dice","prefix":"$"}}' \
    <<<"$(body Plugin '`Acme.Plugin.Dice`' 1.0.0 dice '`$`')"

parse reads-a-rule-set '{"list":"ruleSets","entry":{"id":"Acme.Rules.Approval","version":"1.0.0"}}' \
    <<<"$(body 'Rule set' Acme.Rules.Approval 1.0.0)"

parse reads-windows-line-endings '{"list":"ruleSets","entry":{"id":"Acme.Rules.Approval","version":"1.0.0"}}' \
    <<<"$(body 'Rule set' Acme.Rules.Approval 1.0.0 | sed 's/$/\r/')"

parse refuses-no-kind refuses "Plugin" <<<"$(body '_No response_' Acme.Plugin.Dice 1.0.0 dice)"
parse refuses-no-package refuses "package identifier" <<<"$(body Plugin '' 1.0.0 dice)"
parse refuses-no-version refuses "version" <<<"$(body Plugin Acme.Plugin.Dice '' dice)"
parse refuses-a-plugin-without-a-namespace refuses "namespace" <<<"$(body Plugin Acme.Plugin.Dice 1.0.0)"
parse refuses-a-rule-set-with-a-namespace refuses "Leave both empty" \
    <<<"$(body 'Rule set' Acme.Rules.Approval 1.0.0 approval)"

# Whatever a person writes is a string to jq and nothing else. A value that would mean
# something to a shell comes out as the text it is, for gate.sh to refuse as an identifier.
parse keeps-hostile-text-as-text '{"list":"plugins","entry":{"id":"$(touch pwned)","version":"1.0.0","namespace":"dice","prefix":null}}' \
    <<<"$(body Plugin '$(touch pwned)' 1.0.0 dice)"
[[ ! -e pwned && ! -e "$here/pwned" ]]
check runs-none-of-it $?

echo
echo "insert:"

# The ledger on main, through the writer and back. Any difference here is a submission that
# would rewrite lines it did not add.
diff <(tr -d '\r' < "$root/ledger/submitted.json") <(bash "$here/../insert.sh" "$root/ledger/submitted.json" | tr -d '\r') > "$work/diff" 2>&1
check keeps-the-ledger-as-it-is $? "$(cat "$work/diff")"

printf '%s' '{"list":"plugins","entry":{"id":"Acme.Plugin.Dice","version":"1.0.0","namespace":"dice","prefix":null}}' > "$work/dice.json"
bash "$here/../insert.sh" "$root/ledger/submitted.json" "$work/dice.json" > "$work/head.json"

added=$(diff <(tr -d '\r' < "$root/ledger/submitted.json") <(tr -d '\r' < "$work/head.json") | grep -c '^>')
removed=$(diff <(tr -d '\r' < "$root/ledger/submitted.json") <(tr -d '\r' < "$work/head.json") | grep -c '^<')
[[ "$added" -eq 1 && "$removed" -eq 0 ]]
check adds-one-line $? "added $added, removed $removed"

jq -e '.plugins | map(.id) == (map(.id) | sort)' "$work/head.json" >/dev/null
check puts-it-in-order $?

printf '%s' '{"list":"ruleSets","entry":{"id":"Zeta.Rules.Last","version":"1.0.0"}}' > "$work/last.json"
bash "$here/../insert.sh" "$root/ledger/submitted.json" "$work/last.json" > "$work/last-head.json"
jq -e '.ruleSets[-1].id == "Zeta.Rules.Last"' "$work/last-head.json" >/dev/null
check puts-a-last-name-last $?

echo
echo "end to end:"

body Plugin Acme.Plugin.Dice 1.0.0 dice > "$work/e2e.md"
bash "$here/../parse.sh" "$work/e2e.md" > "$work/parsed.json"
bash "$here/../insert.sh" "$root/ledger/submitted.json" "$work/parsed.json" > "$work/e2e.json"
output=$(bash "$here/../../admit/gate.sh" "$root/ledger/submitted.json" "$work/e2e.json" "$root/ledger/reserved.json")
check admits-a-new-plugin $? "$output"

body Plugin Acme.Plugin.Grid 1.0.0 grid > "$work/taken.md"
bash "$here/../parse.sh" "$work/taken.md" > "$work/taken.json"
bash "$here/../insert.sh" "$root/ledger/submitted.json" "$work/taken.json" > "$work/taken-head.json"
output=$(bash "$here/../../admit/gate.sh" "$root/ledger/submitted.json" "$work/taken-head.json" "$root/ledger/reserved.json")
[[ $? -eq 1 && "$output" == *"already holds"* ]]
check refuses-a-held-namespace $? "$output"

first=$(jq -r '.plugins[0].id' "$root/ledger/submitted.json")
body Plugin "$first" 9.9.9 other > "$work/again.md"
bash "$here/../parse.sh" "$work/again.md" > "$work/again.json"
bash "$here/../insert.sh" "$root/ledger/submitted.json" "$work/again.json" > "$work/again-head.json"
output=$(bash "$here/../../admit/gate.sh" "$root/ledger/submitted.json" "$work/again-head.json" "$root/ledger/reserved.json")
[[ $? -eq 1 && "$output" == *"already in the ledger"* ]]
check refuses-a-package-already-in-the-ledger $? "$output"

echo
if [[ $failed -gt 0 ]]; then
    echo "$failed of $((passed + failed)) failed."
    exit 1
fi

echo "$passed cases, all as expected."
