﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

public abstract class TestBase : IDisposable
{
    private const int GCAllocationAttempts = 10;

    private static TimeSpan TestTimeout => Debugger.IsAttached ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(5);
    private readonly CancellationTokenSource timeoutTokenSource;

    protected static readonly TimeSpan ExpectedTimeout = TimeSpan.FromMilliseconds(200);

    protected static readonly TimeSpan UnexpectedTimeout = Debugger.IsAttached ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(5);

    protected static CancellationToken ExpectedTimeoutToken => new CancellationTokenSource(ExpectedTimeout).Token;

    protected TestBase(ITestOutputHelper logger)
    {
        this.Logger = logger;
        this.timeoutTokenSource = new CancellationTokenSource(TestTimeout);
    }

    protected ITestOutputHelper Logger { get; }

    protected CancellationToken TimeoutToken => this.timeoutTokenSource.Token;

    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.timeoutTokenSource.Dispose();
        }
    }

    /// <summary>
    /// Runs a given scenario many times to observe memory characteristics and assert that they can satisfy given conditions.
    /// </summary>
    /// <param name="scenario">The delegate to invoke.</param>
    /// <param name="maxBytesAllocated">The maximum number of bytes allowed to be allocated by one run of the scenario. Use -1 to indicate no limit.</param>
    /// <param name="iterations">The number of times to invoke <paramref name="scenario"/> in a row before measuring average memory impact.</param>
    /// <param name="allowedAttempts">The number of times the (scenario * iterations) loop repeats with a failing result before ultimately giving up.</param>
    /// <returns>A task that captures the result of the operation.</returns>
    protected async Task CheckGCPressureAsync(Func<Task> scenario, int maxBytesAllocated = -1, int iterations = 100, int allowedAttempts = GCAllocationAttempts)
    {
        // prime the pump
        for (int i = 0; i < 3; i++)
        {
            await scenario();
        }

        // This test is rather rough.  So we're willing to try it a few times in order to observe the desired value.
        bool passingAttemptObserved = false;
        for (int attempt = 1; attempt <= allowedAttempts; attempt++)
        {
            this.Logger?.WriteLine($"Attempt {attempt}");
            long initialMemory = GC.GetTotalMemory(true);
            for (int i = 0; i < iterations; i++)
            {
                await scenario();
            }

            long allocated = (GC.GetTotalMemory(false) - initialMemory) / iterations;
            long leaked = (GC.GetTotalMemory(true) - initialMemory) / iterations;

            this.Logger?.WriteLine($"{leaked} bytes leaked per iteration.", leaked);
            this.Logger?.WriteLine($"{allocated} bytes allocated per iteration ({maxBytesAllocated} allowed).");

            if (leaked <= 0 && (allocated <= maxBytesAllocated || maxBytesAllocated < 0))
            {
                passingAttemptObserved = true;
                break;
            }

            if (!passingAttemptObserved)
            {
                // give the system a bit of cool down time to increase the odds we'll pass next time.
                GC.Collect();
                await Task.Delay(250);
            }
        }

        Assert.True(passingAttemptObserved);
    }
}
