using Xunit;

// CoreStringsTests mutates CoreStrings.Language (a Core-layer static, process-global setting - by
// design, since Core has no per-request/per-user context to hang it off of; the real App only ever
// has one active language at a time too). xUnit parallelizes different test classes across threads
// by default, and SystemCheckerTests/others call Core methods that read CoreStrings.Language
// implicitly without ever setting it themselves - a real, reproduced failure during this project's
// own test authoring (see PROJECT_STATUS.md, CoreStrings increment): SystemCheckerTests failed
// non-deterministically with a Ukrainian string where a Russian one was expected, because
// CoreStringsTests had set Language="uk" on another thread mid-run. Disabling collection
// parallelization for the whole assembly is the simplest fix that doesn't require every existing
// test (which never anticipated a mutable global language setting) to defensively reset it.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
