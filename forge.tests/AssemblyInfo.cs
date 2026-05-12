// Phase 4 Phase-A — Disable parallel test-collection execution.
//
// Two `WebApplicationFactory<Program>` collections (`Integration` and the
// new `Capabilities` one) racing on the same `Program` entry point produces
// "The entry point exited without ever building an IHost" failures. The
// individual integration tests inside one collection are still inherently
// non-parallelized; this attribute only prevents the two collections from
// spinning up factories concurrently.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
