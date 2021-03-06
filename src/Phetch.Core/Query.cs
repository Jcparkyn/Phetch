namespace Phetch.Core;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// An asynchronous query taking one parameter of type <typeparamref name="TArg"/> and returning a
/// result of type <typeparamref name="TResult"/>
/// </summary>
/// <remarks>
/// <para>For queries with no parameters, you can use the <see cref="Query{TResult}"/> class.</para>
/// <para>For queries with multiple parameters, you can use a tuple in place of <c>TArg</c>:
/// <code>Query&lt;(int, string), string&gt;</code>
/// </para>
/// </remarks>
public class Query<TArg, TResult>
{
    private readonly QueryCache<TArg, TResult> _cache;
    private readonly QueryOptions<TArg, TResult> _options;
    private FixedQuery<TArg, TResult>? _lastSuccessfulQuery;
    private FixedQuery<TArg, TResult>? _currentQuery;

    /// <summary>
    /// An event that fires whenever the state of this query changes.
    /// </summary>
    public event Action StateChanged = delegate { };

    /// <summary>
    /// An event that fires whenever this query succeeds.
    /// </summary>
    public event Action<QuerySuccessContext<TArg, TResult>>? Succeeded;

    /// <summary>
    /// An event that fires whenever this query fails.
    /// </summary>
    public event Action<QueryFailureContext<TArg>>? Failed;

    internal Query(
        QueryCache<TArg, TResult> cache,
        QueryOptions<TArg, TResult>? options = null)
    {
        _cache = cache;
        _options = options ?? QueryOptions<TArg, TResult>.Default;
        Succeeded += options?.OnSuccess;
        Failed += options?.OnFailure;
    }

    /// <summary>
    /// Creates a new Query from a query function.
    /// </summary>
    public Query(
        Func<TArg, CancellationToken, Task<TResult>> queryFn,
        QueryOptions<TArg, TResult>? options = null
    ) : this(
        new QueryCache<TArg, TResult>(queryFn, EndpointOptions<TArg, TResult>.Default),
        options)
    { }

    /// <summary>
    /// The current status of this query.
    /// </summary>
    public QueryStatus Status => _currentQuery?.Status ?? QueryStatus.Idle;

    /// <summary>
    /// The response data from the current query if it exists.
    /// </summary>
    /// <remarks>
    /// To also keep data from previous args while a new query is loading, use <see
    /// cref="LastData"/> instead.
    /// </remarks>
    public TResult? Data => _currentQuery is not null
        ? _currentQuery.Data
        : default;

    /// <summary>
    /// The response data from the current query if it exists, otherwise the response data from the
    /// last successful query.
    /// </summary>
    /// <remarks>
    /// This is useful for pagination, if you want to keep the data of the previous page visible
    /// while the next page loads. May return data from a different query argument if the argument
    /// has changed.
    /// </remarks>
    public TResult? LastData => IsSuccess
        ? _currentQuery.Data
        : _lastSuccessfulQuery?.Status == QueryStatus.Success
            ? _lastSuccessfulQuery.Data
            : default;

    /// <summary>
    /// The exception returned by the last query failure, or <c>null</c> if the query has never failed.
    /// </summary>
    public Exception? Error => _currentQuery?.Error;

    /// <summary>
    /// True if the query is currently loading and has not previously succeeded with the same argument.
    /// </summary>
    /// <remarks>
    /// This will return <c>false</c> if the query is currently re-fetching due to the current data
    /// being stale. Use <see cref="IsFetching"/> for these cases (e.g., to show a loading indicator).
    /// </remarks>
    public bool IsLoading => _currentQuery?.Status == QueryStatus.Loading;

    /// <summary>
    /// True if the query threw an exception and has not been re-run.
    /// </summary>
    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsError => Status == QueryStatus.Error;

    /// <summary>
    /// True if the query has succeeded.
    /// </summary>
    /// <remarks>
    /// In many cases you should prefer to use <see cref="HasData"/> as it works better with
    /// nullable reference types.
    /// </remarks>
    [MemberNotNullWhen(true, nameof(_currentQuery))]
    public bool IsSuccess => _currentQuery?.Status == QueryStatus.Success;

    /// <summary>
    /// True if the query has succeeded and returned a non-null response.
    /// </summary>
    /// <remarks>
    /// This is particularly useful in combination with nullable reference types, as it lets you
    /// safely access <see cref="Data"/> without a compiler warning.
    /// </remarks>
    [MemberNotNullWhen(true, nameof(Data))]
    public bool HasData => IsSuccess && Data is not null;

    /// <summary>
    /// True if no arguments have been provided to this query yet.
    /// </summary>
    public bool IsUninitialized => Status == QueryStatus.Idle;

    /// <summary>
    /// True if the query is currently running, either for the initial load or for subsequent
    /// fetches once the data is stale.
    /// </summary>
    /// <remarks>
    /// If you only need to know about the initial load, use <see cref="IsLoading"/> instead.
    /// </remarks>
    public bool IsFetching => _currentQuery?.IsFetching ?? false;

    /// <summary>
    /// Stop listening to changes of the current query.
    /// </summary>
    public void Detach()
    {
        // TODO: Consider redesign
        _currentQuery?.RemoveObserver(this);
        _currentQuery = null;
    }

    /// <summary>
    /// Runs the original query function once, completely bypassing caching and other extra behaviour
    /// </summary>
    /// <param name="arg">The argument passed to the query function</param>
    /// <param name="ct">An optional cancellation token</param>
    /// <returns>The value returned by the query function</returns>
    public Task<TResult> Invoke(TArg arg, CancellationToken ct = default)
    {
        return _cache.QueryFn.Invoke(arg, ct);
    }

    /// <summary>
    /// Cancel the currently running query using the <see cref="CancellationToken"/> that was passed to it.
    /// </summary>
    public void Cancel() => _currentQuery?.Cancel();

    /// <summary>
    /// Re-runs the query using the most recent argument, without waiting for the result.
    /// </summary>
    /// <remarks>
    /// To also return the result of the query, use <see cref="RefetchAsync"/>.
    /// </remarks>
    /// <inheritdoc cref="RefetchAsync" path="/exception"/>
    public void Refetch()
    {
        _currentQuery?.Refetch();
    }

    /// <summary>
    /// Re-runs the query using the most recent argument and returns the result asynchronously.
    /// </summary>
    /// <returns>The value returned by the query function</returns>
    /// <exception cref="InvalidOperationException">Thrown if no argument has been provided to the query</exception>
    public Task<TResult> RefetchAsync()
    {
        if (_currentQuery is null)
            throw new InvalidOperationException("Cannot refetch an unititialized query");

        return _currentQuery.RefetchAsync();
    }

    /// <summary>
    /// Updates the argument for this query, and re-run the query if the argument has changed.
    /// </summary>
    /// <remarks>
    /// If you need to <c>await</c> the completion of the query, use <see cref="SetArgAsync"/> instead.
    /// </remarks>
    public void SetArg(TArg arg) => _ = SetArgAsync(arg);

    /// <summary>
    /// Updates the argument for this query, and re-run the query if the argument has changed.
    /// </summary>
    /// <remarks>
    /// If you do not need to <c>await</c> the completion of the query, use <see cref="SetArg"/> instead.
    /// </remarks>
    /// <returns>
    /// A <see cref="Task"/> which completes when the query returns, or immediately if there is a
    /// non-stale cached value for this argument.
    /// </returns>
    public async Task SetArgAsync(TArg arg)
    {
        var newQuery = _cache.GetOrAdd(arg);
        if (newQuery != _currentQuery)
        {
            _currentQuery?.RemoveObserver(this);
            newQuery.AddObserver(this);
            _currentQuery = newQuery;
            // TODO: Is this the best behaviour?

            if (!newQuery.IsFetching && newQuery.IsStaleByTime(_options.StaleTime, DateTime.Now))
            {
                await newQuery.RefetchAsync().ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc cref="TriggerAsync"/>
    public void Trigger(TArg arg) => _ = TriggerAsync(arg);

    /// <summary>
    /// Run the query function without sharing state or cache with other queries.
    /// </summary>
    /// <remarks>
    /// This is typically used for "mutations", which are queries that have side effects (e.g., POST
    /// requests). This has the following differences from <see cref="SetArgAsync(TArg)"/>:
    /// <list type="bullet">
    /// <item>
    /// This will always run the query function, even if it was previously run with the same query argument.
    /// </item>
    /// <item>
    /// The state of this query (including the cached return value) will not be shared with other
    /// queries that use the same query argument.
    /// </item>
    /// </list>
    /// </remarks>
    /// <param name="arg">The argument to pass to the query function</param>
    public async Task<TResult> TriggerAsync(TArg arg)
    {
        // TODO: Re-use when arguments unchanged?
        var query = _cache.AddUncached(arg);
        _currentQuery?.RemoveObserver(this);
        query.AddObserver(this);
        _currentQuery = query;
        return await query.RefetchAsync().ConfigureAwait(false);
    }

    internal void OnQuerySuccess(QuerySuccessContext<TArg, TResult> context)
    {
        _lastSuccessfulQuery = _currentQuery;
        Succeeded?.Invoke(context);
        StateChanged?.Invoke();
    }

    internal void OnQueryFailure(QueryFailureContext<TArg> context)
    {
        Failed?.Invoke(context);
        StateChanged?.Invoke();
    }

    internal void OnQueryUpdate()
    {
        StateChanged?.Invoke();
    }
}

/// <summary>
/// An alternate version of <see cref="Query{TArg, TResult}"/> for queries with no parameters.
/// </summary>
/// <remarks>Aside from having no parameters, this functions identically to a normal Query</remarks>
public class Query<TResult> : Query<Unit, TResult>
{
    /// <summary>
    /// Creates a new Query from a query function with no parameters.
    /// </summary>
    public Query(
        Func<CancellationToken, Task<TResult>> queryFn,
        QueryOptions<Unit, TResult>? options = null
    ) : base((_, ct) => queryFn(ct), options)
    { }

    internal Query(
        QueryCache<Unit, TResult> cache,
        QueryOptions<Unit, TResult>? options = null
    ) : base(cache, options)
    { }

    /// <summary>
    /// Causes this query to fetch if it has not already.
    /// </summary>
    /// <remarks>
    /// This is equivalent to <see cref="Query{TArg, TResult}.SetArg(TArg)"/>, but for paramterless queries.
    /// </remarks>
    public void Fetch() => _ = SetArgAsync(default);

    /// <inheritdoc cref="Fetch"/>
    public Task FetchAsync() => SetArgAsync(default);

    /// <inheritdoc cref="Query{TArg, TResult}.Trigger(TArg)"/>
    public void Trigger() => _ = TriggerAsync(default);

    /// <inheritdoc cref="Query{TArg, TResult}.TriggerAsync(TArg)"/>
    public Task<TResult> TriggerAsync() => TriggerAsync(default);
}

/// <summary>
/// An alternate version of <see cref="Query{TArg, TResult}"/> for queries with no return value.
/// </summary>
/// <remarks>Aside from having no return value, this functions identically to a normal Query</remarks>
public class Mutation<TArg> : Query<TArg, Unit>
{
    /// <summary>
    /// Creates a new Mutation from a query function with no return value.
    /// </summary>
    public Mutation(
        Func<TArg, CancellationToken, Task> mutationFn,
        QueryOptions<TArg, Unit>? endpointOptions = null
    ) : base(
        async (arg, ct) =>
        {
            await mutationFn(arg, ct);
            return new Unit();
        },
        endpointOptions)
    {
    }

    internal Mutation(
        QueryCache<TArg, Unit> cache,
        QueryOptions<TArg, Unit>? options = null
    ) : base(cache, options)
    {
    }
}
