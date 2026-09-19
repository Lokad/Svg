# C# engineering guidelines

These rules apply to production code, tests, tools and benchmarks.

- Do not use default parameter values. Callers pass every parameter; named convenience overloads may forward explicit values.
- Update internal constructors, interfaces, implementations and all consumers together. Published APIs require deliberate versioned changes.
- Prefer non-nullable contracts. Validate nullable, external and deserialized values at their boundary; never use the null-forgiving operator.
- Use precise domain types rather than unrelated primitive values.
- Keep a helper with one caller as a local function, avoiding hot-path closure allocations.
- Document every abstract or virtual implementor contract, including ownership, cancellation, budget accounting and thread safety.
- Do not put `InternalsVisibleTo` in a project file. A narrow, documented source-level assembly attribute is required if one is ever necessary.

Compiler warnings are errors. Public API tests also reject optional parameters.
