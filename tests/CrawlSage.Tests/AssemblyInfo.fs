module CrawlSage.Tests.AssemblyInfo

// Run test collections sequentially. A few tests assert genuine concurrency (the peak number
// of in-flight requests), which needs the thread pool to itself — running test classes in
// parallel starves it on slow/contended CI runners and makes those assertions flaky.
[<assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)>]
do ()
