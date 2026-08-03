# Regression corpus

Every file in this directory is replayed through **all** public entry points by
`RobustnessTests.Corpus_EveryCheckedInCrasher_IsWellBehaved`, and must satisfy the same
contract as any other input:

> A public entry point either returns, or throws `ArgumentException` (or a subclass).
> `IndexOutOfRangeException`, `NullReferenceException`, `OverflowException`,
> `InvalidCastException`, `KeyNotFoundException`, `ArithmeticException` and
> `FormatException` are defects, not input rejection.

## What belongs here

The exact bytes of any input that ever broke that contract — found by the fuzz sweeps, by
a bug report, or by a real capture off an instrument. Drop the file in, and it becomes a
permanent regression test.

Name files after what they broke, e.g. `pe-truncated-escape-run.plt`, and keep them small:
the point is the shortest input that reproduces the failure, not the capture it came from.

## Why it is empty

Nothing has broken the contract yet. The sweeps in `RobustnessTests.cs` — exhaustive
truncation of `feature-exercise.plt`, strided truncation of the two larger fixtures, seeded
byte-corruption and drop/insert mutation across three fixtures, and an explicit hostile-input
set — come to roughly 2,600 renders per target framework and found no violation.

That is a result, not an omission. The directory and the test exist so that the first
counterexample is one committed file away from never happening again.
