#!/usr/bin/env bash
#
# What the site does with a catalogue written by somebody hostile.
#
# Every entry in the catalogue is derived by loading a package, and loading a package is
# running its code. That is [what this registry says it does not police](../../../doc/policy.md),
# so the pages built from it are written as though a publisher will one day put something in
# their assembly and their .nuspec on purpose. These are those two cases.
#
#   tool/Site/test/run.sh

set -uo pipefail

here=$(cd "$(dirname "$0")" && pwd)
root=$(cd "$here/../../.." && pwd)
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

failed=0

report() {
    if [[ $1 -eq 0 ]]; then
        printf '  ok    %s\n' "$2"
    else
        printf '  FAIL  %s\n' "$2"
        failed=$((failed + 1))
    fi
}

echo "site:"

# 1. An operation name that is a path. It becomes site/op/<name>.html, so a name carrying ..
# would be written outside the folder — over the front page, in the case below. The generator
# has to refuse the catalogue rather than write any of it.
mkdir -p "$work/name/site"
printf 'the front page\n' > "$work/name/site/index.html"
output=$(cd "$root" && dotnet run --project tool/Site -c Release -- \
    tool/Site/test/hostile-name "$work/name/site/pages" 2>&1)
status=$?

[[ $status -ne 0 ]]
report $? "refuses an operation name that is a path"

grep -q 'not operation names' <<<"$output"
report $? "says which name it refused"

grep -qx 'the front page' "$work/name/site/index.html"
report $? "wrote nothing outside the output folder"

# 2. A .nuspec that is trying to write the page rather than describe a package. The strings
# are the publisher's, so they may say anything; what they may not do is leave the text.
mkdir -p "$work/url"
(cd "$root" && dotnet run --project tool/Site -c Release -- \
    tool/Site/test/hostile-url "$work/url" > /dev/null 2>&1)
report $? "renders a catalogue whose strings are hostile"

hrefs=$(grep -ho 'href="[^"]*"' -r "$work/url" | sort -u)

! grep -qi 'javascript:' <<<"$hrefs"
report $? "does not put a javascript: URL in an href"

# The pages only. The catalogue's JSON carries the publisher's string as their string — it is
# the API, and its readers are the ones who escape it, exactly as this generator does.
! grep -rqF --include='*.html' "<script>alert('description')</script>" "$work/url"
report $? "escapes a description that is markup"

# `checked` is the catalogue's own, not a publisher's, but it reaches the footer of every page
# and there is no reason for it to be the one string that goes through unescaped.
! grep -rqF --include='*.html' "<script>alert(1)</script>" "$work/url"
report $? "escapes the checked timestamp"

grep -rq --include='*.html' 'Last checked' "$work/url"
report $? "says when the catalogue was checked"

# 3. A catalogue from before there was a timestamp to say. Leaving the line off is the honest
# answer; inventing one, or refusing to render, would both be worse.
mkdir -p "$work/plain"
(cd "$root" && dotnet run --project tool/Site -c Release -- tool/Site/test/plain "$work/plain" > /dev/null 2>&1)
report $? "renders a catalogue that does not say when it was checked"

! grep -rq --include='*.html' 'Last checked' "$work/plain"
report $? "leaves the line off rather than inventing one"

# No plugin in it reserves a shorthand character either, and the front page names the ones
# that are in use in the middle of a sentence.
grep -q 'In use: <span class="none">none</span>' "$work/plain/index.html"
report $? "says none rather than an empty list of shorthand characters"

# 4. A plugin with releases the catalogue would not index. Their publisher is the only party
# who can put that right and the only one nothing tells, so the pages are where it is said: the
# release is on them, marked, carrying what it claimed against what the ledger admits. What is
# withheld is the indexing — the version does not become `latest` and its operations are not
# offered — and not the fact.
mkdir -p "$work/withheld"
(cd "$root" && dotnet run --project tool/Site -c Release -- tool/Site/test/withheld "$work/withheld" > /dev/null 2>&1)
report $? "renders a catalogue with a withheld release"

page="$work/withheld/plugin/Withheld.Rules.html"

grep -q 'not indexed' "$page"
report $? "marks the withheld release"

grep -q '<code>acme</code>' "$page"
report $? "says the namespace the withheld release claimed"

grep -q 'the ledger admits <code>held</code>' "$page"
report $? "says what the ledger admits instead"

grep -q 'Nothing could be read out of this release' "$page"
report $? "says when a release could not be read at all"

# The one that would be silently wrong. Taking the newest release rather than the newest
# indexed one leaves a plugin with no operations at all and a latest nobody may resolve to.
grep -q '<th>Latest</th><td><code>1.1.0</code>' "$page"
report $? "reports the newest indexed release as the latest"

[[ -f "$work/withheld/op/held.two.html" ]]
report $? "writes the operations of the newest indexed release"

grep -q '2 withheld' "$work/withheld/index.html"
report $? "marks the plugin on the claim table"

# 5. Every release withheld. There is no latest to name and no operation to offer, and the
# plugin is on the pages anyway — it is the case where saying so matters most.
mkdir -p "$work/all"
(cd "$root" && dotnet run --project tool/Site -c Release -- tool/Site/test/all-withheld "$work/all" > /dev/null 2>&1)
report $? "renders a catalogue whose every release is withheld"

grep -q 'none indexed' "$work/all/plugin/Nothing.Rules.html"
report $? "says there is no indexed release"

[[ -z "$(ls -A "$work/all/op")" ]]
report $? "offers no operation from a plugin with no indexed release"

# 6. Rule sets. A composite's page has to answer the question somebody arrives with — what does
# this hold, and can I get it — so the two halves worth checking are that a held rule set this
# index can answer for is a link, and that one it cannot is not.
mkdir -p "$work/ruleset"
(cd "$root" && dotnet run --project tool/Site -c Release -- tool/Site/test/ruleset "$work/ruleset" > /dev/null 2>&1)
report $? "renders a catalogue with rule sets in it"

page="$work/ruleset/ruleset/Acme.Rules.Roster.html"

grep -q 'href="Acme.Rules.Shift.html"' "$page"
report $? "links a held rule set that is indexed"

# The link that must not be written. `approval` is a name nothing here can answer for, and a
# link to a page that does not exist would say the opposite of what is true.
! grep -q 'href="approval.html"' "$page"
report $? "does not link a held rule set that is not indexed"

grep -q 'A held rule set that is not indexed here is one nothing can fetch' "$page"
report $? "says what an unindexed held rule set means"

# A component that shipped in the same package as the document holding it. There is no page to
# link to and nothing is wrong: it arrived with the thing that holds it. Warning about it would
# report the one case where holding something unindexed is right as the case where it is wrong.
grep -q 'Acme.Rules.Roster.Slot ^1.0</code> as <code>slot</code> <span class="meta">ships with it' "$page"
report $? "says a held rule set that ships in the same package ships with it"

! grep -q 'href="Acme.Rules.Roster.Slot.html"' "$page"
report $? "does not link a part that has no page"

# A vocabulary nobody publishes is a supported arrangement, so a `requires` naming one is
# rendered rather than refused — and not linked, for the reason above.
! grep -q 'href="../plugin/Acme.Deploy.Rules.html"' "$page"
report $? "does not link a required plugin that is not indexed"

grep -q '<code>assign</code>' "$page"
report $? "lists the inputs a composite could constrain"

component="$work/ruleset/ruleset/Acme.Rules.Shift.html"

grep -q 'declares the identifier <code>shift</code>' "$component"
report $? "says the identifier the withheld release declared"

grep -q '<th>Latest</th><td><code>1.0.0</code>' "$component"
report $? "reports the newest indexed release as the latest"

grep -q '1 withheld' "$work/ruleset/index.html"
report $? "marks the rule set on the front page"

# The example a reader copies. `as` carries the short name, which is the whole reason a
# package-shaped identifier costs a holding document nothing to write.
grep -q '"as": "roster"' "$page"
report $? "writes a uses example whose alias is short"

[[ -f "$work/ruleset/ruleset/Acme.Rules.Roster.json" ]]
report $? "publishes the entry beside the page"

# 7. A catalogue from before rule sets were indexed. Every fixture above this one has no
# `ruleSets` at all, and the front page has to say so rather than render an empty table.
grep -q 'None yet' "$work/plain/index.html"
report $? "says none rather than an empty table of rule sets"

echo

# 8. The search. It is the one part of this site that is a program rather than a page, and the
# cases for it are a program too — the script is lifted out of the rendered page and run, so
# what is tested is what is served rather than a copy of it kept in step by hand.
if ! command -v node >/dev/null; then
    echo "  FAIL  the search cases need node, and it is not here"
    failed=$((failed + 1))
else
    (cd "$root" && node tool/Site/test/search.mjs "$work/ruleset")
    report $? "the search finds what it is given"
fi

echo
if [[ $failed -gt 0 ]]; then
    echo "$failed failed."
    exit 1
fi

echo "all as expected."
