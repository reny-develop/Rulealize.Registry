# The registry — the design, and what it is for

What a plugin ecosystem needs that a package feed does not give it, and why the answer is
an index rather than a host.

**Decided, not built.** Nothing in this document exists yet. It settles what will be built,
in what order, and what will deliberately never be built.

- Assumes: [Rulealize](https://github.com/reny-develop/Rulealize), and
  [the standard vocabulary](https://github.com/reny-develop/Rulealize/blob/main/doc/plugin.md)

## 1. The three questions nuget.org cannot answer

Distribution is solved. A plugin is a .NET assembly, it has a package feed, and there is no
reason to build a second one. What is missing is everything a feed does not model.

| The question | On nuget.org | Here |
| --- | --- | --- |
| Which plugin provides `grid.ray`? | unanswerable — a feed indexes packages, not what is inside them | an operation is an indexed entity |
| What satisfies this rule set's `requires`? | fourteen `git clone` lines | resolved from the index |
| Is `acme` taken? Is `%` taken? | **the concept does not exist** | the ledger, and nothing else can be |

The third is the reason to build anything at all.

`OperationTable.Claim` refuses a plugin whose identifier, namespace or shorthand character
another plugin already claimed. That check is right, and it runs **at the wrong end of the
timeline** — at the moment somebody assembles a plugin folder, which is after both plugins
were published and after rule sets naming them went into production. The runtime cannot do
better; it only ever sees the plugins in front of it. **No participant in the ecosystem sees
two plugins that have never been loaded together, which is exactly the pair that collides.**

A registry is the only party that does. Moving that check earlier is not a convenience on
top of a package list — it is the one thing here that has no other home, and the one thing
that cannot be retrofitted, because by the time it is wanted the colliding names are already
spent.

## 2. What is not being built

**Not a package host.** NuGet distributes plugins; a repository and a tag distribute a rule
set. The registry stores an index and points at both.

**No manifest file. Not in any phase.**
[`PluginProbe`](https://github.com/reny-develop/Rulealize/blob/main/src/Internal/Plugin/PluginProbe.cs)
says why: a plugin is a public `IRulealizePlugin` with a parameterless constructor, and
nothing else marks one — "no attribute, no naming convention, no manifest file beside the
DLL — because the interface is already the contract and a second declaration could only
disagree with it."

A registry that asks a publisher to fill in a form describing their plugin has invented that
second declaration, one step further from the binary than the file that was already refused.
So **a submission carries a pointer, not a description**: a package identifier, or a
repository and a tag. Everything else is derived (§3).

**No accounts, no upload, no web submission form** until there is a second publisher. GitHub
already is the identity system (§7).

## 3. Where the facts come from — the validator is an ordinary host

```
submission:  a package id on nuget.org         (plugin)
             a repository and a tag            (rule set)
```

CI fetches it and **loads it the way an application does**. For a plugin, that is
`LoadPluginsFrom` over the extracted package — the same folder scan a deployed application
runs, which is already the arrangement the Rulealize tests and samples use rather than a
project reference. For a rule set, it is `CreateContext` against the plugins its `requires`
resolves to.

Nothing in the registry knows more about a plugin than an application does, and there is no
path by which the index can describe a plugin the runtime would not.

| Derived by loading | Taken from the repository |
| --- | --- |
| identifier, version, namespace, reserved prefix | README, `doc/specification.md`, license |
| every operation, with its kind and its plugin | the source of the tagged build |
| the `Rulealize.Abstraction` version it was built against | |

The Abstraction version is worth indexing because it is a whole class of failure a user
otherwise meets as `ReflectionTypeLoadException`, which `PluginProbe` can only report as
"most likely built against a different version of `Rulealize.Abstraction`". A compatibility
column answers it before the download.

Prose is **linked, not copied**, for the reason
[plugin.md](https://github.com/reny-develop/Rulealize/blob/main/doc/plugin.md#why-the-specifications-are-not-in-this-repository)
already gives for specifications living with their plugin: a specification describes one
version of one vocabulary and has to be able to change when that plugin releases and not
before. A copy inside the index would ship on the index's schedule and would eventually be
wrong.

### 3.1 The one thing the runtime has to add

`RuleRuntime` exposes `Plugins`, the manifests. It does not expose what those plugins
registered — the operation names live in `OperationTable`, which is internal. **A runtime
cannot currently be asked what it can do.**

```csharp
public ImmutableArray<OperationDescriptor> Operations { get; }   // op, kind, plugin id
```

The registry needs it to index operations at all. It is not a registry feature: the same
read-only view is what turns `'grid.rey' is not a known operation` into `did you mean
'grid.ray'?`, and it is what an editor completing an `op` value would ask for. **The whole
demand this design makes on the runtime is one property**, and it is one nobody would regret
having.

## 4. Three kinds of entry

| | Submitted | Keyed by | Valid when |
| --- | --- | --- | --- |
| **plugin** | yes | package identifier | it loads, and its claims are free |
| **operation** | **no — derived** | `grid.ray` | its plugin is |
| **rule set** | yes | `<publisher>/<id>` | it compiles |

An operation has no independent existence and no submission of its own. It is a projection
of §3, which is what keeps the index from having a second, drifting account of what a plugin
provides.

### 4.1 Two documents, because only one of them is read by a person

| | [`ledger/claim.json`](../ledger/claim.json) | the catalogue |
| --- | --- | --- |
| in git | **yes** — the diff *is* the review | no, generated on every run |
| granularity | one row per plugin | one entry per **version** |
| holds | the claims, and the version they were admitted at | descriptions, repositories, licences, every version's operations |
| built by | [`tool/Ledger`](../tool/Ledger/) | [`tool/Catalogue`](../tool/Catalogue/) |

The granularity is forced from opposite ends. A claim is
[permanent](policy.md#a-claim-is-permanent), so a plugin has exactly one set of claims for
its whole life and a second row could only ever contradict the first. But `requires` reads
`^1.0`, and operations may be added within a major, so resolving a constraint needs the
version list and the operations of each.

**A plugin entry carries no hand-written prose at all.** The description, the repository, the
licence and the abstraction it was built against are already in the `.nuspec`; the operations
and the claims come from the assembly. A submission is a package identifier, which is what §2
meant by carrying a pointer rather than a description — and it turned out to cost nothing,
because a package feed already requires publishers to write all of it down.

Rule sets are the exception, and the reason is that they have no `.nuspec`. A rule set
document yields its `id`, `version`, `requires`, inputs and state fields, and nothing about
who published it or what it is for. **They are the only place hand-written prose enters the
registry**, which is one more reason they are worth doing after plugins rather than alongside.

### 4.2 What a new version costs

Nothing. Its claims are unchanged by definition, so no committed file moves and no pull
request is opened; the next catalogue run finds it on nuget.org and it appears.

What that would otherwise let through is a plugin changing its namespace quietly between
versions, so **the catalogue checks every version's claims against the ledger** and refuses
to write anything if one disagrees. The permanence rule is enforced mechanically, on a
schedule, while the file a human reads does not move — which is the shape the whole split was
for.

## 5. Rule sets are the second currency

Almost every package ecosystem has one kind of thing in it. This one has two, and the reason
is a claim Rulealize makes about itself: **a rule set is data — it ships, versions and diffs
on its own, and the same host binary runs a different set of rules.** That claim needs
somewhere to publish one, or it stays a property of a repository nobody visits.

Four things follow from admitting them, none of which apply to plugins.

### 5.1 The key needs a publisher scope, and the document does not change

A plugin identifier is vendor-qualified already — `Rulealize.Plugin.Grid`,
`Acme.Deploy.Rules` — because `requires` names plugins **across** documents, so those names
have to be globally unique inside the document itself.

A rule set identifier is bare: `"id": "chess"`. The second person to publish chess collides
with the first. But a rule set's identifier is only ever read within one deployment — a
state document names `chess@1.0.0` to say which rules it belongs to, and nothing outside
names it at all. **So the scope can be added outside the document**: the registry key is
`reny/chess`, and `chess.json` is published unmodified. Vendor-qualifying rule set
identifiers the way plugins are qualified would be paying, in every document, for a
uniqueness only the index requires.

### 5.2 Validity is machine-checkable, and it is the same check the library advertises

Everything decidable about a rule set is decided in `CreateContext`. So the registry's
verdict on a submitted rule set is not a review — it is a compile, and the badge says
exactly what happened:

```
compiles against Grid 1.1.0, Sequence 1.2.0, State 1.0.0, … — checked 2026-08-11
```

Re-run nightly, this also catches the ecosystem-level event nothing else observes: a plugin
releases 2.0, and every rule set whose `requires` said `^1.0` stops resolving. That is
precisely what `^` was cut down to three constraint forms to express, and the registry is
the only place its consequences are visible in aggregate.

### 5.3 The bytes have to be somewhere

The repository and tag are the truth; the registry caches a copy of the JSON. It cannot run
§5.2 without the bytes, and a few kilobytes of text is not what "not a package host" was
protecting against.

### 5.4 A rule set is content, not code

It needs a license field of its own, and the submission policy needs one line that plugins
never needed: **the rules of a commercial game are somebody's, and a gallery of rule sets
invites exactly that submission.** Better answered once, in writing, than per pull request.

## 6. The ledger

Three claims, three quite different scarcities.

| | Scarcity | Policy |
| --- | --- | --- |
| identifier | effectively unbounded | vendor-qualify; first come |
| namespace | short, memorable, and written into every operation name | first come, plus a reserved set |
| **shorthand prefix** | **one character, perhaps a dozen usable** | **granted by review; by default, nobody gets one** |

Three are spent already — `@` for Binding, `$` for State, `#` for Definition — out of a set
one keystroke wide that cannot be extended, so
[plugin.md](https://github.com/reny-develop/Rulealize/blob/main/doc/plugin.md#the-conventions)'s
advice — "a vocabulary with an audience of one should not spend one" — is **promoted from a
convention to the grant policy**. The published table of what is taken is the registry's
plainest artifact and its most durable one.

Reserved ahead of anyone asking: the twelve standard namespaces, and the handful an obvious
future standard plugin would want (`str`, `time`, `set`, `fmt`). A squatter on `str` costs
the ecosystem a name it cannot buy back.

**A ledger is only a defence while other people can read it.** Which sets the one deadline
in this document: it goes public **before** the first third-party plugin ships, not after.
Published later, it records collisions instead of preventing them, and §1's argument for
building any of this is spent.

## 7. What the registry cannot check, and must not imply

**Loading a plugin is running its code.** Discovery is a folder scan with no signature and
no sandbox — reasonable for the runtime, and the thing an ecosystem changes, because the
DLLs stop all being yours. The site has to say so as plainly as a package feed does.

**Purity cannot be verified.** Every operation is required to be a pure function of its
arguments (`GetValidInputs` evaluates a guard once per candidate, so an impure operation
turns a domain into a query storm and answers one question two ways inside one call). No
check CI can run establishes it, and it is a defect that only appears under a large domain —
which is to say, in production and not in a submission. **A badge that reads as though
purity were checked would be worse than no badge**, so "verified" is allowed to mean one
thing only: *CI reproduced this package from the tagged source.*

## 8. Where it runs

GitHub, and the fit is not luck — every dynamic thing this registry needs already has a
GitHub-shaped answer, which is why no server appears in any phase.

| Need | Answer |
| --- | --- |
| identity | GitHub accounts |
| submission | a pull request |
| validation | Actions, running §3 |
| watching for new releases | a scheduled workflow that opens a pull request |
| download counts | NuGet's own, embedded |
| artifacts | nuget.org, and repository tags |
| the site | Pages |
| **the API** | **static JSON on that same Pages** |

The last line is the one that decides the architecture. The catalogue is published at stable
URLs and that *is* the API:

| | |
| --- | --- |
| `/index.json` | every plugin and every operation, in summary. What search reads |
| `/plugin/<id>.json` | one plugin, all versions, all operations. What the resolver reads |

The Phase 3 resolver (§9) needs one file per entry in a rule set's `requires`, and it is a
static file. Search is client-side over the index, which at 10 KB for the standard
distribution is not a compromise but the faster answer — the round trip that would fetch one
operation is larger than the file holding all of them.

There is no `/op/<name>.json`, though an earlier draft of this document said there would be.
An operation's data is already in its plugin's file and duplicated in the index; a third copy
would buy a request nobody makes. `/op/grid.ray` is a page, not a document.

### 8.1 The site is dark; the repository is not

The repository is the production system and the page is a rendering of it. Only the second
one is held back.

Two facts settle it. [Access control for GitHub
Pages](https://github.blog/changelog/2021-01-21-access-control-for-github-pages/) — a
published site that requires read access — is **GitHub Enterprise Cloud only**, so a private
page is not purchasable here at any tier. And a private repository cannot receive pull
requests from non-collaborators, so **"private" and "run the real submission process from
day one" contradict each other** at the repository, though not at the page.

So: **repository public from the first commit; site unlinked, `noindex`, and labelled
pre-release.** What had to be identical to production — submission, validation, review policy
— is identical. What the page's visibility changes is nobody's experience but the author's.
It is also the cheaper side: Actions minutes are unmetered for public repositories.

One trap to avoid. Holding the deploy step means the site generator is the one part of the
pipeline never exercised, and it will be broken on the day it matters. **CI builds the site
on every pull request and uploads it as an artifact; only publishing is behind the flag.**

If authenticated viewing ever does become necessary, Cloudflare Pages plus Cloudflare Access
serves the same static output on free tiers — at the cost of a custom domain and a second
platform, which is not worth paying while the audience is one person.

### 8.2 Analytics, and the escape hatch

A beacon — Cloudflare Web Analytics, or GoatCounter — is free, cookieless, needs no consent
banner, and works on Pages without hosting anything. Self-hosted analytics is a running cost
and is refused on that ground alone.

And the choice is reversible: the output is a static directory, so Cloudflare Pages is a
drop-in if edge compute is ever wanted. **Choosing Pages closes no door**, which is the only
reason to decide this now rather than later.

## 9. The phases

**1 — the ledger, and a curated index.** Submissions by pull request; CI performs §3 and
diffs the claims against the ledger. Site generated, not published. The point of doing the
ledger first is §6: it is the only part that cannot be retrofitted, and at the end of this
phase it is complete.

**2 — ingestion.** Half of this turned out to be free: a *new version* of a plugin already
admitted needs no pull request and no discovery, because §4.2 leaves nothing to commit and
the scheduled catalogue run finds it. What is left is discovering a plugin nobody has
submitted — querying nuget.org for packages depending on `Rulealize.Abstraction` and opening
the pull request that would admit one. That is when the registry stops being a gate and
becomes a view, and it is gated on §6's deadline.

**3 — the resolver.** `rulealize restore ruleset/reversi.json` reads `requires`, resolves it
against the index, and materialises a plugin folder.

**The resolver is the actual product; the site is its visualisation.** Rulealize's README
currently opens the door with fourteen `git clone` lines. The ecosystem exists on the day
that is one command.

## 10. Decided, settled by building it, and still open

### Decided

- An index over NuGet, **not a package host**
- Facts are derived by loading the artifact. **No manifest file, in any phase** (§2)
- Three kinds of entry; operations are derived and never submitted (§4)
- Rule sets are first class, keyed `<publisher>/<id>` **without changing the document**, and
  validated by compiling them (§5)
- The ledger is the registry's reason to exist. Prefixes are granted by review; by default
  none (§6)
- "Verified" means CI reproduced the build from tagged source, and nothing about purity (§7)
- Public repository, dark page, GitHub Pages, static JSON as the API (§8)
- One addition to the runtime: `RuleRuntime.Operations` (§3.1)

### Settled by building it

- **`RuleRuntime.Operations` is built**, and §3.1's claim held: it is one read-only property.
  Pointed at the standard distribution it reports **68 operations across the twelve
  plugins** — 53 expressions, 6 effects and 9 schema nodes
- **[`tool/Ledger`](../tool/Ledger/) is built**, and it is what §3 said it would be: a host
  that calls `LoadPluginsFrom` and writes down what came back. It names no plugin, carries no
  list of the standard twelve, and reads nothing but the DLLs
- **The ledger document is `rulealize/registry/ledger/v1`**
  ([`ledger/claim.json`](../ledger/claim.json)), and every choice in it serves being read as
  a diff: plugins and operations sorted, so a folder scan's order cannot appear as a change;
  no timestamp and no tool version, so a regeneration that found nothing produces no diff;
  operations grouped by kind rather than listed as objects carrying one, which keeps a new
  operation to a single line — and gives the name registered as two kinds somewhere to appear
  twice, which a map from name to kind could not
- **Phase 1's check is built** ([`.github/workflows/ledger.yml`](../.github/workflows/ledger.yml)):
  it trusts nothing in a submitted ledger but the package identifiers and versions, fetches
  those, re-derives the file and fails on any difference. Deliberately not scheduled — the
  ledger pins a version per plugin, so a scheduled re-derivation would reproduce the same
  file for ever; noticing a new release is Phase 2's job and not this one's
- **An absent claim is written, not omitted.** `"prefix": null`, and all three kinds present
  even when a plugin registers none of one. A ledger records claims, and "claimed no
  shorthand character" is a claim
- **[`tool/Catalogue`](../tool/Catalogue/) is built**, and against the real feed it produces
  12 plugin entries and a 10 KB index. Its check refuses to write anything at all when a
  version's claims disagree with the ledger, which was worth having the negative case for:
  a partial catalogue written beside a failure is a catalogue somebody will serve
- **The catalogue loads no plugin, and cannot.** `PluginProbe` uses `Assembly.LoadFrom` into
  the default context so that a plugin's `Rulealize.Abstraction` resolves to the one already
  in memory — right for a host, and it means **one process cannot hold two versions of the
  same plugin assembly**. So the catalogue runs `tool/Ledger` once per version in a process
  of its own and reads its output. The tool that fetches touches no plugin; the tool that
  loads touches no network
- **`version` became `admitted`** in the ledger. The field had come to mean two things —
  which version to fetch, and which version the claims were reviewed at — and only the second
  survives now that the catalogue checks the rest
- **Third-party prose is escaped as third-party prose.** A description is whatever a publisher
  put in their `.nuspec` and a page will eventually put it on screen, so the catalogue keeps
  `< > & ' "` escaped while letting other scripts through unescaped — a Japanese description
  stays itself instead of becoming six times its length in `\uXXXX`
- **A namespace cannot be reserved before its package exists**, which had been left open here
  and is settled in [the grant policy](policy.md#no-claim-before-a-package). It turned out not
  to be the judgement call it looked like: an entry is derived by loading an assembly, so a
  reservation could only be a hand-written claim no artifact backs — §2's second declaration,
  arriving by a different door. Refusing it costs a publisher the risk of being beaten to a
  name, and the answer to that is to publish `0.1.0` on the day the name is chosen

### Blocked, and how it reordered the phases

**Phase 1's CI could not be built at all.** Validation by loading (§3) needs the assemblies,
and the only way to obtain the twelve was to build fourteen sibling repositories from source:
neither Rulealize nor `Rulealize.Abstraction` nor any plugin was on nuget.org, and the
abstraction was consumed from a folder feed each developer produced locally.

So the ledger could be generated on a workstation and could not be re-derived by a pull
request, which is the whole of what §3 promises. **Publishing the packages turned out not to
be a task running alongside the registry but a precondition of it**, and the phase order in
§9 had assumed otherwise.

Resolved by publishing all fourteen, which was the smaller half of the surprise: every
project already carried its package metadata, and all fourteen packed clean on the first
attempt. What it cost was the decision, not the work — the pre-release notice came off
Rulealize's README, and the ecosystem's entry point stopped being fourteen `git clone` lines.

Two consequences worth recording, because neither was obvious before doing it:

- **The tool takes Rulealize as a package, not as a project reference to a sibling.** That is
  what lets it run on a machine that has cloned this repository and nothing else, which is
  the entire requirement CI places on it
- **`NuGet.config` stays, and stays useful.** With no `<clear/>` a folder feed is added to
  nuget.org rather than replacing it, so an unpublished change to the abstraction can still
  be tried out locally while everything published resolves from nuget.org. The file that
  looked like scaffolding to delete on publication turned out to be the thing that makes
  publication non-disruptive

### Open

- **The rule set entry.** The plugin half of the index is settled (§4.1); rule sets are not,
  and they are where the hand-written half of a submission lives. Deliberately left until the
  plugin half is running end to end
- **When the site goes public.** The constraint is one-sided — before the first third-party
  plugin, not after (§6) — and the date is not chosen
- **Whether a rule set may require a plugin that is not in the index at all**, which is the
  `AddPlugin` case: `sample/Deploy` requires `Acme.Deploy.Rules`, a vocabulary that is
  correctly not published anywhere. Such a rule set cannot be compiled by CI, so §5.2 has no
  verdict to give it — and refusing it would exclude precisely the documents that
  demonstrate the feature
