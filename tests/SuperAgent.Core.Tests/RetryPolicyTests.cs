using Xunit;
using SuperAgent.Core;
using System.Threading;
using System.Threading.Tasks;

namespace SuperAgent.Core.Tests;

public class RetryPolicyTests
{
    [Fact]
    public async Task RetryPolicy_SucceedsOnFirstAttempt()
    {
        var policy = new RetryPolicy(maxRetries: 3);
        var attempts = 0;

        var result = await policy.ExecuteAsync(async (ct) =>
        {
            attempts++;
            return 42;
        }, "TestOperation", CancellationToken.None);

        Assert.Equal(42, result);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task RetryPolicy_RetriesOnFailure()
    {
        var policy = new RetryPolicy(maxRetries: 3, initialDelay: TimeSpan.Zero);
        var attempts = 0;

        var ex = await Assert.ThrowsAsync<AggregateException>(async () =>
        {
            await policy.ExecuteAsync(async (ct) =>
            {
                attempts++;
                throw new InvalidOperationException($"Attempt {attempts} failed");
            }, "TestOperation", CancellationToken.None);
        });

        // 應該重試 3 次後失敗 (總共 4 次嘗試：1 次初始 + 3 次重試)
        Assert.Equal(4, attempts);
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public async Task RetryPolicy_SucceedsAfterRetry()
    {
        var policy = new RetryPolicy(maxRetries: 3, initialDelay: TimeSpan.Zero);
        var attempts = 0;

        var result = await policy.ExecuteAsync(async (ct) =>
        {
            attempts++;
            if (attempts < 2)
            {
                throw new InvalidOperationException($"Attempt {attempts} failed");
            }
            return attempts;
        }, "TestOperation", CancellationToken.None);

        Assert.Equal(2, result);
        Assert.Equal(2, attempts);
    }
}