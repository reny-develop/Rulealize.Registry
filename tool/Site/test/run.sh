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

echo
if [[ $failed -gt 0 ]]; then
    echo "$failed failed."
    exit 1
fi

echo "all as expected."
