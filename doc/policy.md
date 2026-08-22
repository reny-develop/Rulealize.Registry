# The grant policy

What may be claimed, on what grounds, and what can never be taken back. This is what a
submission is held to.

## Why there is a ledger at all

`OperationTable.Claim` refuses a plugin whose identifier, namespace or shorthand character
another plugin already claimed. That check is right, and it runs **at the wrong end of the
timeline** — at the moment somebody assembles a plugin folder, which is after both plugins
were published and after rule sets naming them went into production. The runtime cannot do
better; it only ever sees the plugins in front of it. **No participant in the ecosystem sees
two plugins that have never been loaded together, which is exactly the pair that collides.**

An index is the only party that does, and moving that check earlier is the one thing here
that cannot be retrofitted: by the time it is wanted, the colliding names are already spent.
**A ledger is only a defence while other people can read it**, which is why
[`ledger/claim.json`](../ledger/claim.json) and
[the table generated from it](https://reny-develop.github.io/Rulealize.Registry/) were public
before the first third-party plugin shipped. Published later, a ledger records collisions
instead of preventing them.

## The three claims

A plugin claims three things, and they are not equally scarce.

| | How much of it there is | How it is granted |
| --- | --- | --- |
| identifier | unbounded | first come, vendor-qualified |
| namespace | short, memorable, written into every operation name | first come, outside a reserved set |
| shorthand character | **fewer than a dozen will ever exist** | **by review. The default answer is no** |

The identifier is nuget.org's to allocate and this registry only records it. The other two
are this ecosystem's alone, and no package feed models either.

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

**Reserved.** Those the standard distribution holds, and a short list held against
plugins that do not exist yet — `str`, `time`, `set`, `fmt`. These are refused rather than
granted. A general name the standard distribution would obviously want should not be spent by
whoever asked first, because unlike an identifier there is no supply of others.

**Vendor-qualify a private vocabulary.** `acme`, not `deploy`. A namespace with an audience
of one still occupies a name in a space everyone shares, which is why
[Rulealize's conventions](https://github.com/reny-develop/Rulealize/blob/main/doc/plugin.md#the-conventions)
already say so for vocabularies that will never be submitted here.

## Shorthand characters

A plugin may reserve one character. A string literal beginning with it is handed to that
plugin's expander instead of being read as text, so `"@c"` is a node and not the two
characters it looks like.

**Three are spent**: `@` for Binding, `$` for State, `#` for Definition. What is left is one
keystroke wide, cannot be extended, and shrinks further on inspection — a character ordinary
data might begin with would silently swallow it, so letters, digits, `-` and `.` are out, and
a character carrying meaning *inside* a value, like the separator in a tuple's text form,
should not also begin one. **What remains is under a dozen characters for the entire future
of the ecosystem.**

So the default answer is no, and a grant has to clear all four of these.

1. **The vocabulary is in the standard distribution, or is demonstrably in broad use.** A
   vocabulary with an audience of one should not spend one
2. **The expansion is a reference to something named** — a binding, a state field, a
   definition — rather than an operation taking arguments. All three that exist are
   references, and that is not a coincidence: an operation with arguments has nowhere to put
   them inside a string literal
3. **It appears often enough that writing it out obscures the rule.** The test is a real rule
   set with the shorthand expanded. If it still reads, the shorthand was a preference
4. **The character is one no ordinary value would begin with**

A refusal costs one plugin some verbosity. A grant costs every future plugin one of the last
characters, which is why the burden falls the way it does.

## No claim before a package

**A namespace cannot be reserved in advance.** Not as a courtesy, and not for a plugin that
is nearly ready.

This is not a judgement call — it follows from how the ledger is made. Every entry is derived
by loading an assembly, and there is nothing to load before a package exists. A reservation
would have to be a claim written by hand that no artifact backs — the second declaration
[this registry refuses](../README.md#what-it-will-never-be), arriving by a different door.

The cost is real: a namespace can be taken while somebody is still building against it. The
answer is to publish `0.1.0` on the day the namespace is chosen. That is cheap, it is what
every package feed already expects, and it makes the claim in the only way this registry is
able to record one.

The reserved list above is the one hand-written thing here, and it is the opposite of a
claim: it grants nothing to anybody and exists only to refuse.

## A claim is permanent

A namespace is not released when a plugin is abandoned, unlisted, or deleted from nuget.org.

A rule set names plugins in `requires`, and it is a document that outlives its author's
interest in maintaining them. Handing `grid` to somebody else would not break a build — it
would change what an existing document means, quietly, in whichever deployment updates its
plugin folder next. No recovered name is worth that.

Ownership moves with the package. If nuget.org says an identifier changed hands, so does
everything the ledger records under it.

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

**Whether an operation is a good idea.** If it loads and its claims are free, it is in.

Nothing here will ever be labelled "verified" on any of those grounds. A badge that read as
though purity or safety had been checked would be worse than no badge, so the word is kept
for one claim and one meaning: CI reproduced this package from its tagged source.

## How to claim

Open a pull request adding the plugin to [`ledger/claim.json`](../ledger/claim.json). CI
fetches that package, re-derives the entry and fails on any difference, so the only part you
can get wrong is which package you named.

**Derive your entry rather than write it.** The tool that produced that file produces your row
too: from a clone of this repository, point it at a folder holding your plugin and, given no
output file, it writes what your assembly claims to standard output.

```sh
dotnet run --project tool/Ledger -- <your plugin folder>
```

What comes back is a whole ledger for that folder — a `$schema` and a `plugins` array — so a
`plugin/` folder that `restore` has filled describes the standard vocabularies beside yours.
Take your object out of that array, and put it in this one in identifier order, indented as it
was printed:

```json
    {
      "id": "Acme.Deploy.Rules",
      "admitted": "0.1.0",
      "namespace": "acme",
      "prefix": null,
      "operations": {
        "expression": [
          "acme.frozen"
        ],
        "effect": [],
        "schema": []
      }
    },
```

`admitted` is the version those claims were read from, which is your `PluginManifest`'s and
not your project file's. CI asks nuget.org for the package at exactly that string, so a
manifest and a package version that have drifted apart fail here as a restore error rather
than as a diff — the one failure in this file whose cause is not written on it. Raise the two
together.

All three kinds of operation are written even where you register none of one, and a plugin
claiming no shorthand character writes `null` rather than leaving the field out — "claimed no
shorthand character" is a claim, and one worth being able to see was made.

The comparison is `diff -u` against a regenerated file, so the ordering and the layout above
are part of what has to match. Pasting what the tool printed is why none of that is something
you have to know.

Nothing else is submitted. The description, repository, licence and the version of
`Rulealize.Abstraction` it was built against are read from the package; there is no field
here for anything you would otherwise have to keep in step with a release.

A claim that collides shows up as a conflict with an existing entry, and is refused there
rather than in somebody's application six months later. That is the whole of the exercise.
