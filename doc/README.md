# Documentation

| | |
| --- | --- |
| [The grant policy](policy.md) | what may be claimed, on what grounds, and what can never be taken back |
| [Publishing to this index](publish.md) | what to build, so that a submission has something true to say |

The first is binding. It is what a submission is held to, and it is written to be read by the
person about to open the pull request rather than by whoever maintains this repository.

The second is not binding and holds no rule of its own — every constraint in it is the
policy's, linked where it appears. It exists because a rule set is distributed as a package
that carries no assembly, which is a shape nothing else in this ecosystem produces, and a rule
nobody can act on is a rule stated badly.

It lives here rather than in the Rulealize repository for the reason that repository already
applies to plugin specifications: a document belongs with the thing it describes, so that it
can change when that thing does and not on somebody else's release schedule. Which namespaces
are spoken for is not a fact about the runtime, and the runtime is not where it should be
recorded.

The claim ledger the policy governs exists as data — [`ledger/submitted.json`](../ledger/submitted.json),
one line per package, every line held to the package it names by [`tool/Ledger`](../tool/Ledger/)
— which loads a plugin's assembly and reads a rule set's document, in one sweep of one folder.
What that finds goes into the catalogue built by [`tool/Catalogue`](../tool/Catalogue/), which
is committed nowhere because it is derived from nuget.org on every run. [The readme](../README.md)
describes both, and why only one of them is in git.
