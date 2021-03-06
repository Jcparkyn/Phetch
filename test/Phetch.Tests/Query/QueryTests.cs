namespace Phetch.Tests.Query
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using FluentAssertions;
    using FluentAssertions.Execution;
    using Phetch.Core;
    using Xunit;

    public class QueryTests
    {
        [Fact]
        public async Task Should_work_with_basic_query()
        {
            var query = new Query<string>(
                _ => Task.FromResult("test")
            );
            await query.SetArgAsync(default);

            query.Data.Should().Be("test");
            query.Status.Should().Be(QueryStatus.Success);
            query.IsSuccess.Should().BeTrue();
            query.IsLoading.Should().BeFalse();
            query.IsFetching.Should().BeFalse();
            query.IsError.Should().BeFalse();
            query.Error.Should().BeNull();
        }

        [Fact]
        public async Task SetArg_should_set_loading_states_correctly()
        {
            var tcs = new TaskCompletionSource<string>();
            var query = new Query<string>(
                _ => tcs.Task
            );

            query.Status.Should().Be(QueryStatus.Idle);
            query.IsUninitialized.Should().BeTrue();
            query.HasData.Should().BeFalse();

            // Fetch once
            var refetchTask = query.SetArgAsync(default);

            query.IsLoading.Should().BeTrue();
            query.IsFetching.Should().BeTrue();

            await Task.Delay(1);
            tcs.SetResult("test");
            await refetchTask;

            query.Status.Should().Be(QueryStatus.Success);
            query.IsSuccess.Should().BeTrue();
            query.IsLoading.Should().BeFalse();
            query.IsFetching.Should().BeFalse();
            query.HasData.Should().BeTrue();

            tcs = new();
            // Fetch again
            var refetchTask2 = query.RefetchAsync();

            query.Status.Should().Be(QueryStatus.Success);
            query.IsLoading.Should().BeFalse();
            query.IsFetching.Should().BeTrue();

            tcs.SetResult("test");
            await refetchTask2;

            query.IsLoading.Should().BeFalse();
            query.IsFetching.Should().BeFalse();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task SetArg_should_reset_state_after_cancel_with_cancelable_query(bool awaitBeforeCancel)
        {
            var tcs = new TaskCompletionSource<string>();
            var query = new Query<int, string>(
                (val, ct) => tcs.Task.WaitAsync(ct)
            );

            using var mon = query.Monitor();

            var task = query.Invoking(x => x.SetArgAsync(1))
                .Should().ThrowExactlyAsync<TaskCanceledException>();
            if (awaitBeforeCancel)
            {
                await Task.Delay(1);
            }
            query.Cancel();

            using (new AssertionScope())
            {
                AssertIsIdleState(query);
                mon.OccurredEvents.Should().SatisfyRespectively(
                    e => e.EventName.Should().Be("StateChanged")
                );
            }
            await task;
            AssertIsIdleState(query);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task SetArg_should_reset_state_after_cancel_with_uncancelable_query(bool awaitBeforeCancel)
        {
            var tcs = new TaskCompletionSource<string>();
            var query = new Query<int, string>(
                (val, _) => tcs.Task
            );

            using var mon = query.Monitor();

            var task = query.SetArgAsync(1);
            if (awaitBeforeCancel)
            {
                await Task.Delay(1);
            }
            query.Cancel();

            using (new AssertionScope())
            {
                AssertIsIdleState(query);
                mon.OccurredEvents.Should().SatisfyRespectively(
                    e => e.EventName.Should().Be("StateChanged")
                );
            }
            tcs.SetResult("1");
            await task;
            AssertIsIdleState(query);
        }

        [Fact]
        public async Task Should_handle_query_error()
        {
            var error = new IndexOutOfRangeException("message");
            var query = new Query<string>(
                _ => Task.FromException<string>(error)
            );

            await query.Invoking(x => x.SetArgAsync(default))
                .Should().ThrowExactlyAsync<IndexOutOfRangeException>();

            using (new AssertionScope())
            {
                query.Data.Should().BeNull();
                query.Status.Should().Be(QueryStatus.Error);
                query.Error.Should().Be(error);
                query.IsError.Should().BeTrue();
                query.IsSuccess.Should().BeFalse();
                query.IsLoading.Should().BeFalse();
            }
        }

        [Fact]
        public async Task Should_always_keep_most_recent_data()
        {
            // Timing:
            // t0 -------- [keep]
            // t1     --------- [keep]
            //        ^ refetch

            var (queryFn, sources) = MakeCustomQueryFn(2);
            var query = new Query<string>(
                queryFn
            );

            query.Status.Should().Be(QueryStatus.Idle);

            query.SetArg(default);

            query.IsLoading.Should().BeTrue();
            query.IsFetching.Should().BeTrue();

            query.Refetch();

            sources[0].SetResult("test0");
            await Task.Delay(1);

            query.Status.Should().Be(QueryStatus.Success);
            query.IsSuccess.Should().BeTrue();
            query.IsLoading.Should().BeFalse();
            query.IsFetching.Should().BeTrue();
            query.Data.Should().Be("test0");

            sources[1].SetResult("test1");
            await Task.Delay(1);

            query.Status.Should().Be(QueryStatus.Success);
            query.IsLoading.Should().BeFalse();
            query.IsFetching.Should().BeFalse();
            query.Data.Should().Be("test1");
        }

        [Fact]
        public async Task Should_ignore_outdated_data()
        {
            // Timing:
            // t0 ------------------- [ignore]
            // t1     --------- [keep]
            //        ^ refetch

            var (queryFn, sources) = MakeCustomQueryFn(2);
            var query = new Query<string>(
                queryFn
            );

            query.Status.Should().Be(QueryStatus.Idle);

            query.SetArg(default);

            query.IsLoading.Should().BeTrue();
            query.IsFetching.Should().BeTrue();

            query.Refetch();

            sources[1].SetResult("test1");
            await Task.Delay(1);

            query.Status.Should().Be(QueryStatus.Success);
            query.IsSuccess.Should().BeTrue();
            query.IsLoading.Should().BeFalse();
            query.IsFetching.Should().BeFalse();
            query.Data.Should().Be("test1");

            sources[0].SetResult("test0");
            await Task.Delay(1);

            query.Status.Should().Be(QueryStatus.Success);
            query.IsLoading.Should().BeFalse();
            query.IsFetching.Should().BeFalse();
            query.Data.Should().Be("test1");
        }

        private static void AssertIsIdleState<TArg, TResult>(Query<TArg, TResult> query)
        {
            query.Status.Should().Be(QueryStatus.Idle);
            query.Error.Should().Be(null);
            query.HasData.Should().BeFalse();
            query.IsError.Should().BeFalse();
            query.IsSuccess.Should().BeFalse();
            query.IsLoading.Should().BeFalse();
            query.IsFetching.Should().BeFalse();
        }

        // Makes a query function that can be called multiple times, using a different TaskCompletionSource each time.
        private static (Func<CancellationToken, Task<string>> queryFn, List<TaskCompletionSource<string>> sources) MakeCustomQueryFn(int numSources)
        {
            var sources = Enumerable.Range(0, numSources)
                .Select(_ => new TaskCompletionSource<string>())
                .ToList();

            var queryCount = 0;
            var queryFn = async (CancellationToken _) =>
            {
                if (queryCount > numSources)
                    throw new Exception("Query function called too many times");
                var resultTask = sources[queryCount].Task;
                queryCount++;
                return await resultTask;
            };
            return (queryFn, sources);
        }
    }
}
