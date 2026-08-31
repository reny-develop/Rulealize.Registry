# The grant policy

What may be claimed, on what grounds, and what can never be taken back. This is what a
submission is held to.

## Why there is a ledger at all

`OperationTable.Claim` refuses a plugin whose identifier or namespace another plugin already
claimed. That check is right, and it runs **at the wrong end of the timeline** — at the moment
somebody assembles a plugin folder, which is after both plugins were published and after rule
sets naming them went into production. The runtime cannot do better; it only ever sees the
plugins in front of it. **No participant in the ecosystem sees two plugins that have never been
loaded together, which is exactly the pair that collides.**

An index is the only party that does, and moving that check earlier is the one thing here
that cannot be retrofitted: by the time it is wanted, the colliding names are already spent.
**A ledger is only a defence while other people can read it**, which is why
[`ledger/submitted.json`](../ledger/submitted.json) and
[the table generated from it](https://reny-develop.github.io/Rulealize.Registry/) were public
before the first third-party plugin shipped. Published later, a ledger records collisions
instead of preventing them.

**A rule set is on the ledger for a weaker reason, and it is worth saying which.** Its
identifier is the package identifier, so nuget.org has already refused the collision and this
ledger is not what prevents it. What the ledger does is make that rule checkable — a package
whose document declares something else is not admitted — and it is the list the daily crawl
reads, which is what catches a release that renames itself after it was admitted. Neither of
those is the argument above. A rule set entry earns its place; it does not carry the same one.

## What is claimed

A plugin claims three things, and they are not equally scarce.

| | How much of it there is | How it is granted |
| --- | --- | --- |
| identifier | unbounded | first come, vendor-qualified |
| namespace | short, memorable, written into every operation name | first come, outside a reserved set |
| shorthand character | fewer than a dozen exist, and they are shared | **not allocated**, outside a reserved set |

The identifier is nuget.org's to allocate and this registry only records it. The namespace is
this ecosystem's alone, and no package feed models it. The shorthand character is recorded
here and granted to nobody — a plugin may reserve one another plugin already reserved.

A rule set claims the first row of that table and nothing else. Its identifier is unbounded,
first come, and where first come is decided is nuget.org — because
**the identifier a document declares is the package identifier it is published under.** A
document claims no namespace and no shorthand character; it draws on the vocabularies its
`requires` names and spends nothing of its own.

## Plugin identifiers

**Vendor-qualify.** `Acme.Deploy.Rules`, not `Rules`. A plugin identifier is what a rule
set's `requires` names, so it is read across documents written by people who have never met.

A vocabulary that is not published at all — an `IRulealizePlugin` implemented in an
application's own assembly and handed to `AddPlugin` — is under the same obligation and gets
no entry here. It cannot collide with the ledger, but it can collide with a package that
comes along later, and by then its rule sets are in production.

**Publish under the identifier your manifest declares.** The runtime does not enforce this —
`requires` names a `PluginManifest.Id`, and where a package with that name lives is the
publisher's business — but two things already depend on the two strings being one string:
this registry fetches a submission by the identifier the ledger records, and
[Rulealize.Cli](https://github.com/reny-develop/Rulealize.Cli) resolves a `requires` by asking
nuget.org for exactly the name the document wrote. A plugin published under a different
package identifier cannot be admitted here and cannot be restored by anybody.

## Rule set identifiers

A rule set declares one, and a document that holds it names that identifier:

```json
{ "id": "Acme.Rules.Approval", "version": "1.0.0" }
```

```json
"uses": [
  { "ruleSet": "Acme.Rules.Approval", "version": "^1.0", "as": "req" }
]
```

**Publish under the identifier your document declares.** The same rule as a plugin's and for
the same reason. The runtime does not enforce it — it only refuses a component whose `id` is
not what `uses` named — but this registry fetches a submission by the identifier the ledger
records, and anything resolving a `uses` has nothing else to go on. A rule set published under
a different package identifier cannot be admitted here and cannot be restored by anybody.

**Vendor-qualify.** `Acme.Rules.Approval`, not `Approval`.

**Write it exactly as the package is published.** The runtime matches a `uses` entry against
the document supplied for it with an ordinal comparison, so `Acme.Rules.Approval` and
`acme.rules.approval` are two rule sets. nuget.org does not agree — a package identifier is
one string to it however it is cased, and the feed serves it lowercased either way — so this
is a place where a fetch succeeds and the document that arrives is refused when it compiles.
This registry compares exactly, for the same reason: a release whose document declares an
identifier that is not, character for character, the one the ledger records is withheld.

**One document per package answers to the package's name**, and it is the one a `uses` naming
that package gets. A package where none does is refused: an identifier that did not name a
package would need something to turn it into one, and that something would have to be
reachable before anybody could restore anything.

A package may ship more than one document, where the rule set it publishes is built out of
parts. Then **every other identifier in it is named under the package's** —
`Acme.Rules.Ordering.Line` inside `Acme.Rules.Ordering`. The prefix is what keeps those from
being a name space nobody allocates: the package identifier is nuget.org's, so everything
under it is too, and no two packages can ship one identifier however many parts either is
built from. [How to build one](publish.md#a-package-may-ship-more-than-one-document).

### There is no short name, and none is wanted

The first of two places a rule set's rules differ from a plugin's rather than repeating them.
The other is [below](#your-own-documents-keep-their-own-names), and both follow from this one.

A namespace is short because it is written into every operation name — `grid.ray` is typed a
hundred times in a document that draws on it — and being short is what makes it worth
governing with a reserved list. **An identifier is written once**, in the `uses` entry of each
document that holds it, and `as` carries the name everything else uses. Inside the holder the
rule set above is `req`: `req.raise` in `fires`, `$req.stage` in a guard, `req` in `held`.

**Which is why `as` is written rather than defaulted.** An alias defaults to the identifier
and may not contain a `.`, so a `uses` entry naming a package-shaped identifier and leaving
`as` out is refused — with a message about a key the author did not write:

```
/uses[0]/as: must be a name without '.', which separates a held rule set from its input.
```

That is the whole of what a long identifier costs a holding document, and it is a short name
that document wanted anyway: nothing reads better for having `Acme.Rules.Approval.raise` in it.

So the identifier being long costs one word on one line, and what that buys is worth more:

- **The collision cannot happen.** Two people cannot both publish `Acme.Rules.Approval`
- **There is no reserved list**, and no general name a document not yet written would
  obviously want is spent by whoever asked first
- **Nothing has to turn an identifier into a package**, so fetching what a `uses` names
  reaches nuget.org and not this index — which stays a place to find what you could not
  already name rather than a service a restore depends on being up

### Your own documents keep their own names

All of the above is about a rule set that is **published**. A project's `approval.json`
declaring `"id": "approval"` is under no obligation here.

That is not the courtesy it looks like, and it is not what
[the plugin rules say about a private vocabulary](#namespaces). A namespace is spent whether
or not anybody publishes it, because two plugin folders can be merged into one and the
collision surfaces there. Rule sets are not resolved that way: a document's components come
from one folder — the one beside it, or the one `--rulesets` names — and a project's
`approval` and somebody else's `Acme.Rules.Approval` can sit in that one folder without
meeting. The only way to collide with a published rule set is to declare its identifier, which
is to say to type somebody else's package name on purpose.

A composite may hold both, and reads plainly when it does:

```json
"uses": [
  { "ruleSet": "approval", "as": "req" },
  { "ruleSet": "Acme.Rules.Shift", "version": "^1.0", "as": "shift" }
]
```

The shape of the identifier says which one is yours.

## Namespaces

Everything an operation is called begins with one: `grid.ray`, `acme.frozen`. **First come**,
recorded when the package is published, with two limits.

**Reserved.** Those a published plugin already holds, and a short list held against plugins
that do not exist yet, in [`ledger/reserved.json`](../ledger/reserved.json) — `str`, `time`,
`set`, `fmt` at the time of writing, and that file rather than this sentence is what CI
refuses a claim against. A general name that a vocabulary not yet written would obviously
want should not be spent by whoever asked first, because unlike an identifier there is no
supply of others.

**Vendor-qualify a private vocabulary.** `acme`, not `deploy`. A namespace with an audience
of one still occupies a name in a space everyone shares, which is why
[Rulealize's conventions](https://github.com/reny-develop/Rulealize/blob/main/doc/plugin.md#the-identifier-and-the-namespace-are-still-claimed)
already say so for vocabularies that will never be submitted here.

## Shorthand characters

A plugin may reserve one character. A string literal beginning with it is handed to that
plugin's expander instead of being read as text, so `"@c"` is a node and not the two
characters it looks like.

**Three are in use**: `@` for Binding, `$` for State, `#` for Definition. **None of them is
taken.** A character is not granted to one plugin here, and reserving one another plugin
already reserved is admitted without comment — the two load together, and a rule set that
would otherwise be ambiguous says which vocabulary it meant by naming it: `"$state:board"`.

That is the whole of why this is not decided first come. Under a dozen characters exist for
the entire future of the ecosystem, and a name that cannot be recovered is worth governing
only while losing it costs something. It does not: whoever reserves `$` second reaches their
own shorthand by writing their namespace in front of it, in the documents where it matters
and nowhere else.

**Reserved.** One kind of character cannot be used at all, by anybody. A character that
ordinary data might *begin* with would silently swallow it, and a character carrying meaning
*inside* a value would swallow somebody else's text form — `|` separates a tuple's components,
and a plugin using `|` would take over every tuple whose first component is empty. Those are
in [`ledger/reserved.json`](../ledger/reserved.json) and are refused. That list is about what
the mechanism cannot survive, not about who asked first, so it stays exactly as it was.

The runtime refuses the rest of that class on its own: `PluginManifest` will not accept a
letter, a digit or whitespace as a shorthand character, whatever this registry thinks.

Two things are worth knowing before reserving one, and neither is a condition.

- **The expansion wants to be a reference to something named** — a binding, a state field, a
  definition. All three that exist are, and that is not a coincidence: an operation with
  arguments has nowhere to put them inside a string literal
- **It wants to appear often enough that writing it out obscures the rule.** The test is a
  real rule set with the shorthand expanded. If it still reads, the shorthand was a preference

## No claim before a package

**A namespace cannot be reserved in advance.** Not as a courtesy, and not for a plugin that
is nearly ready.

This is not a judgement call — it follows from how the ledger is kept. Every entry is checked
against what was published, and there is nothing to read before a package exists. A
reservation would have to be a claim that no artifact backs — the second declaration
[this registry refuses](../README.md#what-it-will-never-be), arriving by a different door.

The cost is real: a namespace can be taken while somebody is still building against it. The
answer is to publish `0.1.0` on the day the namespace is chosen. That is cheap, it is what
every package feed already expects, and it makes the claim in the only way this registry is
able to record one.

**A rule set identifier cannot be reserved here either**, and there is nothing to reserve. It
is a package identifier; publishing `0.1.0` is not the answer to the problem, it *is* the
claim, and nuget.org records it whether this ledger has heard of it or not.

[The reserved list](../ledger/reserved.json) — namespaces and characters both — is the one
thing here that no package backs, and it is the opposite of a claim: it grants nothing to
anybody and exists only to refuse.

## A claim is permanent

This is about the two names that have an owner: a plugin's namespace, and the identifier a
rule set's document declares. A shorthand character is not owned in the first place, so there
is nothing about one to release or to keep.

A namespace is not released when a plugin is abandoned, unlisted, or deleted from nuget.org.

A rule set names plugins in `requires`, and it is a document that outlives its author's
interest in maintaining them. Handing `grid` to somebody else would not break a build — it
would change what an existing document means, quietly, in whichever deployment updates its
plugin folder next. No recovered name is worth that.

Ownership moves with the package. If nuget.org says an identifier changed hands, so does
everything the ledger records under it.

**A release claiming something else is withheld, not admitted.** Publishing a version under a
namespace or a shorthand character the ledger did not record does not move the claim. That
release stays out of the index — its operations are not offered, and `latest` stays at the
newest release that still agrees — while the catalogue and
[the plugin's page](https://reny-develop.github.io/Rulealize.Registry/) carry it marked, with
what it claimed beside what the ledger admits. Nothing is taken from anybody and nothing
resolves to it. The way back is a release that claims what the ledger records.

### A document that renames itself

The same rule, and for a rule set it is the whole of what a release is held to. Two strings
are checked on every version: the identifier the document declares, against the package it was
published under, and the version the document declares, against the version of that package.

Both matter and they fail differently. A release whose `id` moved cannot be resolved by any
`uses` that named the old one — the fetch either misses or returns a document saying something
else, and the runtime refuses it. A release whose `version` moved is worse quietly: the fetch
goes by the package's version and every constraint is answered by the document's, so a package
published at `1.1.0` whose document still says `1.0.0` is downloadable and unresolvable, and
nothing but a party holding both strings can say why.

Neither takes anything from anybody. The release is on the page, marked, carrying what it
declared, and `latest` stays where it was. The way back is a release that declares what the
ledger records.

## What is not policed

**Quality, usefulness, and taste.** The ledger records claims; it does not rank vocabularies.

**Purity.** Every operation is required to be a pure function of its arguments, and nothing
this registry can run establishes that. `GetValidInputs` evaluates a guard once per candidate,
so an impure operation turns a domain into a query storm and answers one question two ways
inside one call — a defect that appears only under a large domain, which is to say in
production and not in a submission. A submission is not asked to attest to purity either,
because an attestation nobody checks reads as one somebody did.

**Safety.** Loading a plugin is running its code: discovery is a folder scan, with no
signature and no sandbox. That is reasonable for a runtime whose plugins are all its own
author's, and it is exactly what an ecosystem changes — so this index says so as plainly as
a package feed does, rather than implying an inspection it does not perform.

That applies to this registry as much as to an application, and it is how every plugin entry
here is derived. The namespace, the shorthand character and every operation in the catalogue
are what a package said when CI loaded it — on a runner, unsigned, with nothing reproduced. A
package that answers one way there and another way in somebody's application is not something
anything here can catch.

What that costs is bounded by the runtime rather than by anything here. Claims that are not
the ones the ledger recorded collide with whoever holds them, and `OperationTable.Claim`
refuses the folder they are both in — so a plugin that says one thing to this registry and
another to a deployment is a plugin that will not load beside the ones it misdeclared.

**None of that reaches a rule set entry.** A document is read and never run: its identifier,
version, `requires`, `uses` and inputs are parsed out of JSON, and there is no assembly, no
folder scan and nothing that could answer differently on a second reading.

The exposure comes back when somebody runs the document, because running one means loading
the vocabularies its `requires` names — but that is their exposure to those plugins, and it is
the same one they would have had naming them directly.

**Whether a rule set compiles.** Nothing here builds one. Doing so would mean fetching every
plugin it requires and running their code, which is the exposure the paragraph above exists to
avoid taking on anyone's behalf, and fetching every document it holds, which would make this a
resolver. A rule set that does not compile is a bad package rather than a false claim, and
the claim is the only thing this registry is about.

**Whether what a rule set holds exists.** A `uses` naming an identifier no package answers to
is recorded and shown, not withheld. A document may legitimately hold one its author keeps
beside it, and withholding the holder for it would mean an entry going quiet whenever an
unrelated one did.

**Whether an operation is a good idea.** If it loads and its claims are free, it is in.

Nothing here will ever be labelled "verified" on any of those grounds. A badge that read as
though purity or safety had been checked would be worse than no badge, so the word is kept
for one claim and one meaning: CI reproduced this package from its tagged source.

## How to claim

Open a pull request adding one line to [`ledger/submitted.json`](../ledger/submitted.json), in
identifier order, to the list for what you are submitting.

### A plugin

Four fields, to `plugins`:

```json
    { "id": "Acme.Deploy.Rules", "version": "0.1.0", "namespace": "acme", "prefix": null },
```

That is the whole submission. **The operations are not in it** — nothing you write here has to
be kept in step with a release, because the only list of what your plugin registers is the one
CI reads out of your assembly.

`version` is the release your claims are read at, and it is your `PluginManifest`'s version
rather than your project file's. CI asks nuget.org for the package at exactly that string, so
a manifest and a package version that have drifted apart are refused here. Raise the two
together.

`prefix` is `null` unless your plugin reserves a [shorthand character](#shorthand-characters),
and it is written rather than left out: "reserves no shorthand character" is a claim, and one
worth being able to see was made. It is recorded rather than granted — another entry may
already carry the same character, and that is not something this registry has an opinion
about.

**Nothing you state is believed.** CI fetches the package, loads it, and refuses the pull
request if the assembly says anything other than what the line says — a different namespace, a
shorthand character you did not declare, a manifest version that is not the one it was fetched
at, or a `PluginManifest.Id` that is not the package you named. You cannot record a claim your
plugin does not make, and there is no field here you can fill in wrongly and be believed.

Nothing else is submitted. The description, repository and licence, the version of
`Rulealize.Abstraction` you built against, and every operation you register are read from the
package.

A claim that collides shows up as a plugin folder that cannot be loaded, and is refused there
rather than in somebody's application six months later. That is the whole of the exercise.

### A rule set

Two fields, to `ruleSets`:

```json
    { "id": "Acme.Rules.Approval", "version": "1.0.0" },
```

**There is no third.** The identifier your document declares is `id`, because it is the
package identifier — writing it twice would be writing something this ledger does not record
and could never disagree with, so an entry that states one is refused rather than ignored.

`version` is the release your claims are read at, and it is the `version` your **document**
declares rather than your project file's. CI fetches the package at exactly that string and
reads the document inside it, so a document and a package version that have drifted apart are
refused here for the same reason a plugin's manifest and package are. Raise the two together.

What you publish is a package with no `lib` folder, whose `ruleset` folder holds a document
declaring the identifier you submitted — and, where that one is built out of parts, those
alongside it:

```
Acme.Rules.Approval.1.0.0.nupkg
└── ruleset/
    └── approval.json      ← "id": "Acme.Rules.Approval"
```

The file may be called anything. **Which document answers to an identifier is read out of the
document**, never off its name — a file gets renamed and an identifier does not, which is the
rule Rulealize.Cli already resolves a held rule set by.

**Nothing you state is believed here either.** CI fetches the package, reads the document, and
refuses the pull request if it declares an identifier that is not the package you named or a
version that is not the one it was fetched at. Your `requires`, your `uses`, your inputs and
everything else about the document are read, never asked for.

## What happens to your pull request

**A submission that adds one line and touches nothing else merges when the checks pass.**
Nobody reads it first. There is nothing left to read: the package is fetched and read, every
word of the line is held to what it says, and a plugin claim that collides with one already
made cannot be loaded beside it. Whether the claim is true is not an opinion anybody here
holds.

What stops one is always one of two things, and **neither of them waits on anybody**.

**Yours to put right.** A version that is not three parts, an entry out of order, a namespace
that is not lowercase, a [reserved](../ledger/reserved.json) name or character, a rule set
entry stating more than its two fields, one package on both lists, a line somebody else's
claim was on, a draft. The reason is written on the pull request, you push a change, and it is
answered again. Nobody else is told, because nobody else could have helped.

**Not a submission.** A pull request that changes anything besides `ledger/submitted.json` is
closed. That is not a judgement about the change — this repository indexes plugins and rule
sets and takes nothing else this way, and an
[issue](https://github.com/reny-develop/Rulealize.Registry/issues) is where anything about the
tools, the site or this document belongs.

Nothing else is on either list. A namespace that is not your vendor's —
`Acme.Deploy.Rules` claiming `deploy` — merges. So does a shorthand character somebody else
already reserved, which is not even a collision: it costs the rule sets that write it a
namespace in front, and nothing else.
