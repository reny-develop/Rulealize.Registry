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

## The three claims

A plugin claims three things, and they are not equally scarce.

| | How much of it there is | How it is granted |
| --- | --- | --- |
| identifier | unbounded | first come, vendor-qualified |
| namespace | short, memorable, written into every operation name | first come, outside a reserved set |
| shorthand character | fewer than a dozen exist, and they are shared | **not allocated**, outside a reserved set |

The identifier is nuget.org's to allocate and this registry only records it. The namespace is
this ecosystem's alone, and no package feed models it. The shorthand character is recorded
here and granted to nobody — a plugin may reserve one another plugin already reserved.

## Identifiers

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
[Rulealize's conventions](https://github.com/reny-develop/Rulealize/blob/main/doc/plugin.md#the-conventions)
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
by loading an assembly, and there is nothing to load before a package exists. A reservation
would have to be a claim that no artifact backs — the second declaration
[this registry refuses](../README.md#what-it-will-never-be), arriving by a different door.

The cost is real: a namespace can be taken while somebody is still building against it. The
answer is to publish `0.1.0` on the day the namespace is chosen. That is cheap, it is what
every package feed already expects, and it makes the claim in the only way this registry is
able to record one.

[The reserved list](../ledger/reserved.json) — namespaces and characters both — is the one
thing here that no package backs, and it is the opposite of a claim: it grants nothing to
anybody and exists only to refuse.

## A claim is permanent

This is about the namespace. A shorthand character is not owned in the first place, so there
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

That applies to this registry as much as to an application, and it is how every entry here is
derived. The namespace, the shorthand character and every operation in the catalogue are what
a package said when CI loaded it — on a runner, unsigned, with nothing reproduced. A package
that answers one way there and another way in somebody's application is not something anything
here can catch.

What that costs is bounded by the runtime rather than by anything here. Claims that are not
the ones the ledger recorded collide with whoever holds them, and `OperationTable.Claim`
refuses the folder they are both in — so a plugin that says one thing to this registry and
another to a deployment is a plugin that will not load beside the ones it misdeclared.

**Whether an operation is a good idea.** If it loads and its claims are free, it is in.

Nothing here will ever be labelled "verified" on any of those grounds. A badge that read as
though purity or safety had been checked would be worse than no badge, so the word is kept
for one claim and one meaning: CI reproduced this package from its tagged source.

## How to claim

Open a pull request adding one line to [`ledger/submitted.json`](../ledger/submitted.json), in
identifier order:

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

## What happens to your pull request

**A submission that adds one line and touches nothing else merges when the checks pass.**
Nobody reads it first. There is nothing left to read: the package is fetched and loaded, every
word of the line is held to what the assembly says, and a claim that collides with one already
made cannot be loaded beside it. Whether the claim is true is not an opinion anybody here
holds.

What stops one is always one of two things, and **neither of them waits on anybody**.

**Yours to put right.** A version that is not three parts, an entry out of order, a namespace
that is not lowercase, a [reserved](../ledger/reserved.json) name or character, a line
somebody else's claim was on, a draft. The reason is written on the pull request, you push a
change, and it is answered again. Nobody else is told, because nobody else could have helped.

**Not a submission.** A pull request that changes anything besides `ledger/submitted.json` is
closed. That is not a judgement about the change — this repository indexes plugins and takes
nothing else this way, and an [issue](https://github.com/reny-develop/Rulealize.Registry/issues)
is where anything about the tools, the site or this document belongs.

Nothing else is on either list. A namespace that is not your vendor's —
`Acme.Deploy.Rules` claiming `deploy` — merges. So does a shorthand character somebody else
already reserved, which is not even a collision: it costs the rule sets that write it a
namespace in front, and nothing else.
