"""
VisionCart response-time benchmark.

Measures what a customer actually waits for, against the Release build served
the way production serves it. Deliberately simple: no warm-up tricks, no
best-of-N, and every percentile comes from the raw sample.

Two things make a benchmark like this lie, and both are handled here:

  * The first request to an ASP.NET Core app pays JIT and EF model building,
    which is real but is a once-per-restart cost, not a per-customer one. It is
    measured and reported separately rather than averaged into the rest.

  * A page that hits the database behaves differently once SQL Server has the
    pages in memory. Warm-up runs are therefore discarded explicitly and
    reported, so nobody has to guess whether they were included.
"""

import argparse
import json
import statistics
import sys
import time
import urllib.error
import urllib.request
from concurrent.futures import ThreadPoolExecutor


def fetch(url, timeout=30):
    """Returns (milliseconds, status, bytes). Never raises for an HTTP error."""
    started = time.perf_counter()
    try:
        with urllib.request.urlopen(url, timeout=timeout) as response:
            body = response.read()
            return (time.perf_counter() - started) * 1000, response.status, len(body)
    except urllib.error.HTTPError as error:
        return (time.perf_counter() - started) * 1000, error.code, 0
    except Exception:
        return (time.perf_counter() - started) * 1000, 0, 0


def percentile(values, p):
    if not values:
        return 0.0
    ordered = sorted(values)
    index = min(int(round(p / 100 * len(ordered) + 0.5)) - 1, len(ordered) - 1)
    return ordered[max(index, 0)]


def measure(base, path, runs, warmup):
    url = base.rstrip("/") + path

    for _ in range(warmup):
        fetch(url)

    samples, status, size = [], 0, 0
    for _ in range(runs):
        ms, status, size = fetch(url)
        samples.append(ms)

    return {
        "path": path,
        "status": status,
        "bytes": size,
        "runs": runs,
        "min": min(samples),
        "p50": percentile(samples, 50),
        "p95": percentile(samples, 95),
        "max": max(samples),
        "mean": statistics.fmean(samples),
    }


def throughput(base, path, seconds, workers):
    """Sustained requests per second at a fixed concurrency."""
    url = base.rstrip("/") + path
    deadline = time.perf_counter() + seconds
    latencies, failures = [], 0

    def worker():
        nonlocal failures
        local = []
        while time.perf_counter() < deadline:
            ms, status, _ = fetch(url)
            if status != 200:
                failures += 1
            local.append(ms)
        return local

    with ThreadPoolExecutor(max_workers=workers) as pool:
        for result in [pool.submit(worker) for _ in range(workers)]:
            latencies.extend(result.result())

    elapsed = seconds
    return {
        "path": path,
        "workers": workers,
        "seconds": seconds,
        "requests": len(latencies),
        "rps": len(latencies) / elapsed,
        "p50": percentile(latencies, 50),
        "p95": percentile(latencies, 95),
        "p99": percentile(latencies, 99),
        "failures": failures,
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--base", default="http://localhost:5299")
    parser.add_argument("--runs", type=int, default=50)
    parser.add_argument("--warmup", type=int, default=5)
    parser.add_argument("--load-seconds", type=int, default=10)
    parser.add_argument("--workers", type=int, default=16)
    parser.add_argument("--label", default="dotnet")
    parser.add_argument("--paths", default="")
    parser.add_argument("--out", default="")
    args = parser.parse_args()

    paths = [p for p in args.paths.split(",") if p] or [
        "/", "/frames", "/try-on", "/health/ready",
    ]

    # The very first request of the process pays JIT and EF model building.
    # Reported on its own, because averaging it in would misrepresent both the
    # cold cost and the steady state.
    cold_ms, cold_status, _ = fetch(args.base.rstrip("/") + paths[0])

    results = [measure(args.base, path, args.runs, args.warmup) for path in paths]
    load = throughput(args.base, paths[0], args.load_seconds, args.workers)

    report = {
        "label": args.label,
        "base": args.base,
        "cold_first_request_ms": cold_ms,
        "cold_status": cold_status,
        "warmup_discarded_per_path": args.warmup,
        "endpoints": results,
        "load": load,
    }

    print(f"\n{args.label}  —  {args.base}")
    print("=" * 74)
    print(f"cold first request: {cold_ms:.0f} ms (status {cold_status}, discarded from the rest)")
    print(f"warm-up discarded : {args.warmup} requests per endpoint\n")
    print(f"{'endpoint':<22}{'status':>7}{'KB':>8}{'min':>8}{'p50':>8}{'p95':>8}{'max':>8}")
    print("-" * 74)
    for r in results:
        print(f"{r['path']:<22}{r['status']:>7}{r['bytes'] / 1024:>8.1f}"
              f"{r['min']:>8.1f}{r['p50']:>8.1f}{r['p95']:>8.1f}{r['max']:>8.1f}")

    print(f"\nsustained load on {load['path']} — {load['workers']} concurrent, {load['seconds']}s")
    print("-" * 74)
    print(f"  {load['requests']} requests, {load['rps']:.0f} req/s, "
          f"{load['failures']} failures")
    print(f"  p50 {load['p50']:.1f} ms   p95 {load['p95']:.1f} ms   p99 {load['p99']:.1f} ms")

    if args.out:
        with open(args.out, "w", encoding="utf-8") as handle:
            json.dump(report, handle, indent=2)
        print(f"\nwritten to {args.out}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
