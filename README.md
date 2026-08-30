# Rulealize.Registry

The index of the [Rulealize](https://github.com/reny-develop/Rulealize) ecosystem: which
plugin provides an operation, which versions satisfy a rule set's `requires`, which namespaces
are already spoken for, which shorthand characters are in use by whom, and which published
rule sets a `uses` can name.

> **Status — early.** What exists is the claim ledger, the tools that check it and build the
> catalogue from it, the jobs that hold a pull request to both, and the site — which is
> [up](https://reny-develop.github.io/Rulealize.Registry/), labelled pre-release. Nothing
> has been submitted from outside the project yet.

## The ledger

[`ledger/submitted.json`](ledger/submitted.json) records what was claimed, in two lists.

A **plugin** claims its identifier, its namespace and its shorthand character. **The first two
have exactly one owner across the whole ecosystem.** The third has none: a character is
recorded here and granted to nobody. An entry is one line and four fields — the package, the
version its claims were read at, the namespace and the shorthand character.

A **rule set** claims one name, and it is the package identifier. An entry is one line and two
fields — the package and the version its document was read at — because everything else about
a document is in the document.

Which identifiers, namespaces and characters are taken is in that file and on the
[site](https://reny-develop.github.io/Rulealize.Registry/), and deliberately not repeated
here. A list in this README would be a second copy of the ledger kept in step by whoever
remembered, which is the same reason the operations are not in an entry.

**The operations are not in one.** Nobody submits a list of what their plugin registers, so
nobody keeps one in step with a release and nobody can get one wrong; they are read out of the
assembly by [`tool/Ledger`](tool/Ledger/), which points a `RuleRuntime` at a folder and writes
down what came back — the same folder scan a deployed application performs. Nor are a rule
set's `requires`, its `uses` or its inputs, which that same tool reads out of the documents in
that same folder, through Rulealize's own readers so that no constraint is parsed twice. Run
it against your own work and it prints what it found:

```sh
dotnet run --project tool/Ledger -- <folder>
```

It knows nothing by name and holds no list of anything.

**Nor is the rest of an entry believed.**
[`.github/workflows/ledger.yml`](.github/workflows/ledger.yml) fetches every package the
ledger names, reads it, and refuses anything that says one thing where the package says
another — a namespace, a shorthand character or a version that is not the assembly's, an
identifier or a version that is not the document's, or a package published under a name that
neither declares. A pull request may state a claim; that job is what decides whether it is
true.

Which is why **a submission that adds one line and touches nothing else merges without anybody
reading it**. [`admit.yml`](.github/workflows/admit.yml) checks that it is that and no more —
the rules are in [`.github/admit/gate.sh`](.github/admit/gate.sh), one case each in
[`.github/admit/test/`](.github/admit/test/) — and then waits for the checks. What it will not
admit waits on nobody either: a submission with something wrong in it says so on the pull
request and is the submitter's to push again, and a pull request that is not a submission is
closed, because **this repository indexes plugins and rule sets and takes nothing else that
way** — anything about the tools or the site belongs in an issue.
[The grant policy](doc/policy.md#what-happens-to-your-pull-request) says which is which.

## The catalogue

The ledger is what a person reviews. The catalogue is what the site and the resolver are
generated from, and nobody reviews it — it is rebuilt from nuget.org on every run and is not
in this repository.

```sh
dotnet publish tool/Ledger -c Release -o work/probe
dotnet run --project tool/Catalogue -- ledger/submitted.json work/probe/Rulealize.Registry.Ledger.dll catalogue
```

| | |
| --- | --- |
| `catalogue/index.json` | every plugin, operation and rule set in summary — 10 KB at the size it is today, which is what makes search a client-side matter |
| `catalogue/plugin/<id>.json` | one plugin, every released version, every operation of each |
| `catalogue/ruleset/<id>.json` | one rule set, every released version, what each holds and requires |

The site publishes those three at the root, which is where anything reading them looks:
`/index.json`, `/plugin/<id>.json`, `/ruleset/<id>.json`.

The ledger holds one line per package because [a claim is permanent](doc/policy.md#a-claim-is-permanent);
the catalogue holds one entry per version because `requires` and `uses` read `^1.0` and what a
release offers may grow within a major. **So a new version of anything already admitted needs
no pull request** — nothing committed changes, and the next scheduled run picks it up.

What that would otherwise let through is a package renaming itself quietly between releases: a
plugin moving its namespace, a rule set moving the identifier its document declares.
[`catalogue.yml`](.github/workflows/catalogue.yml) checks every version against the ledger, and
one that disagrees is **withheld**: it stays in the entry and on the page, marked, carrying
what it claimed, and nothing is indexed off it — not its operations, and not its `latest`. So
the rule is enforced daily, the file a person reads does not move, and the one party who can
put it right is the one it is said to.

Nothing hand-written goes into a catalogue entry. The description, repository and licence are
in the `.nuspec`; a plugin's claims, operations and abstraction version are in the assembly,
and a rule set's identifier, version, `requires`, `uses` and inputs are in the document. A
submission is a package identifier, the version to read it at, and — for a plugin — the two
names it claims. Every one of those is checked against the package before it counts.

The abstraction version is worth recording because it is a whole class of failure a user
otherwise meets as a `ReflectionTypeLoadException`, which the runtime can only report as
"most likely built against a different version of `Rulealize.Abstraction`". Prose is linked
rather than copied, for the reason a specification lives with its own plugin: it describes
one version of one vocabulary and has to change when that plugin releases, so a copy here
would ship on this repository's schedule and would eventually be wrong.

## Why this is not just a page on nuget.org

A package feed distributes plugins and documents perfectly well, and this repository does not
try to replace it. What a feed cannot model is the part that makes a Rulealize plugin a plugin.

**A plugin claims a namespace, and it has exactly one owner across the whole ecosystem.** The
runtime refuses a collision when the plugins are loaded together, which is after both were
published and after rule sets naming them are in production. Nobody in the ecosystem sees two
plugins that have never been loaded together, and that is exactly the pair that collides.

An index is the only party that does. That ledger is this repository's first job and the
only part of it that cannot be added later.

**A rule set has no such name**, and that is a decision rather than a fact about the format.
`uses` names a document by the identifier it declares, and two documents declaring one
identifier are the same collision one step over — but the identifier is written once per
holding document and never appears in an operation name, so nothing about it wants to be
short. Spending nuget.org's names on it instead of minting a second space of them is what
makes the collision impossible rather than governed, and it is why a rule set entry has no
`namespace` field, no reserved list, and two fields where a plugin's has four.

What is left for an index to do is smaller and it is not nothing:

- **A release whose document renames itself.** `Acme.Rules.Approval` 1.0.0 declaring that
  identifier and 1.1.0 declaring `approval` is unresolvable by every `uses` that named it,
  and the failure lands on whoever restores rather than on whoever published. The daily run
  is what sees it first
- **A version that drifts from its package.** `uses` is answered by the version *in the
  document*; the fetch goes by the version *on the package*. A release where those disagree
  is fetchable and unresolvable, and nothing but a party holding both strings can say so
- **The shape of a graph nobody has fetched yet.** Restoring a composite needs the transitive
  closure of `uses` and the union of every document's `requires`. Without an entry per version
  that is a sequential fetch-unzip-read per document, with no way to say what it will cost
  before doing it

## The site

[`tool/Site`](tool/Site/) renders the catalogue as pages: the claim table, a plugin page per
entry, a page per operation — because `grid.ray` is what somebody arrives holding and knowing
which vocabulary owns it is the question nuget.org structurally cannot answer — and a rule set
page per entry, which says what that document holds, what it draws on, and which of its inputs
a composite could constrain.

```sh
dotnet run --project tool/Site -- catalogue site
```

It reads `/index.json`, `/plugin/<id>.json` and `/ruleset/<id>.json` and nothing else — no
reference to Rulealize, none to the catalogue's code — so it is the first consumer of the
published API rather than a second path to the same facts. The search on the front page
fetches `/index.json` like any other client would.

**It is up**: <https://reny-develop.github.io/Rulealize.Registry/>. It carries `noindex` and
says pre-release in its header, because nothing has been submitted to it from outside the
project yet.

Every page says in its footer when the catalogue was last read out of nuget.org, and
`/index.json` carries the same stamp to the second. An index nobody is keeping looks exactly
like one that is — the daily run could stop and every page would go on being served, correct
about a world that had moved. The date is what says otherwise. The same run leaves one line
per check on the [`checks`](../../tree/checks) branch, which is the record that outlives the
ninety days a workflow log is kept for.

CI renders it on every run and uploads it whether or not that run will publish, so the
generator is exercised either way and only the deploy step reads `PUBLISH_SITE`. Holding the
build too would have left the generator as the one part never run, broken on the day it
mattered.

## What it indexes

| | |
| --- | --- |
| **plugin** | submitted as a package identifier, a version, and the two names it claims. Nothing stated is believed until the package says the same |
| **operation** | `grid.ray`, `rec.keys` — read out of the assembly, never submitted |
| **rule set** | submitted as a package identifier and a version. Nothing else: the identifier the document declares is the package identifier, and there is no second name to state |
| **what a rule set holds, requires and offers** | its `uses`, its `requires` and its inputs — read out of the document, never submitted |

### A published rule set

A rule set is a document. A published one is a package with no `lib` folder, whose `ruleset`
folder holds **exactly one** `.json`, and whose document declares the package's own identifier
— [what to build](doc/publish.md#a-rule-set) is a project file that compiles nothing:

```json
{ "id": "Acme.Rules.Approval", "version": "1.0.0" }
```

One document per package, because the identifier **is** the package — a package holding two
documents is one where that stopped being true, and something would have to say which of them
the name meant. As it stands nothing has to: a document that holds this one writes

```json
"uses": [
  { "ruleSet": "Acme.Rules.Approval", "version": "^1.0", "as": "req" }
]
```

and anything that can reach nuget.org can fetch what that names.

**`as` is not optional here.** It is the short name the holding document calls it by and the
one that qualifies its inputs — `req.raise`, never the identifier written out — and an alias
may not contain a `.`, because that is what separates a held rule set from its input. An entry
that leaves `as` out is asking for an alias that *is* the identifier, so a package-shaped one
is refused:

```
/uses[0]/as: must be a name without '.', which separates a held rule set from its input.
```

The message names a key the document did not write, which is worth knowing before meeting it.
So the identifier being long costs a holding document one word, once, and it is a word it was
going to want anyway.

**Fetching one asks nothing of this index.** A `uses` names a package, so getting what it
names is the operation `rulealize restore` already performs for a `requires`, pointed at a
different folder.

**It does not do that yet.** Today it resolves a `uses` against the documents beside the one
it was handed, and reports a rule set that is not there as missing — which is the right
answer while nothing is published and the wrong one the day something is. What the convention
above settles is where it goes when it does: to the feed, and not to here. This index is for
finding a rule set you could not already name, and for saying which of its releases are not
worth resolving to.

The registry indexes the document, and never the case. **A rule set's state is the user's own
file, on their own disk.**

## What it will never be

A package host, or a place that asks a publisher to describe their plugin in a form. A
plugin is a public `IRulealizePlugin` with a parameterless constructor and nothing else
marks one, because the interface is already the contract — so an entry is built by loading
the assembly exactly as an application does, and a submission carries a pointer rather than
a description.

The same holds of a document, and more plainly. A rule set is what the runtime will accept as
one, its identifier and version are keys the core reserves, and its `requires` and `uses` are
read here through Rulealize's own two readers — so nothing about an entry is this repository's
reading of the format, and there is no field a publisher fills in about themselves.

**Nor a resolver.** Which versions are published and where the packages are is the feed's
answer, and `rulealize restore` asks it directly — for a plugin today, and for a rule set on
the same terms whenever it learns to. An index that had to be online for a restore to work
would be a different sort of object than this one.

## License

Apache-2.0 covers what this repository is made of — the tools under [`tool/`](tool/), the
jobs and scripts under [`.github/`](.github/), and the prose.

The ledger is not that. [`ledger/`](ledger/) is a record of who claimed which identifier,
namespace and shorthand character, and it is dedicated to the public domain under
[CC0-1.0](ledger/LICENSE-CC0), as is the catalogue built from it — mirror it, embed it in a
resolver, no attribution asked.

**Anything indexed is licensed by whoever published it** — the assembly and the document
alike. An entry is a pointer to a package and holds nothing of the package itself, so nothing
here states the terms of what it points at.
