// The HTTP harness and the service-level tests share one SQL Server database and
// one pool of frame stock. Running their collections concurrently makes the two
// interfere — a stock top-up in one is an unexplained failure in the other.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
