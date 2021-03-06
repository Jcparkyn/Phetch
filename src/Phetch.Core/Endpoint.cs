namespace Phetch.Core;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Defines an "endpoint" that represents a single query function, usually for a specific HTTP endpoint.
/// </summary>
/// <typeparam name="TArg">
/// The type of the argument passed to the query function. To use query functions with multiple
/// arguments, wrap them in a tuple.
/// </typeparam>
/// <typeparam name="TResult">The return type from the query function</typeparam>
/// <remarks>
/// This is the recommended way to use queries in most cases, and serves as a convenient way to create <see
/// cref="Query{TArg, TResult}"/> instances that share the same cache.
/// </remarks>
public class Endpoint<TArg, TResult>
{
    private readonly EndpointOptions<TArg, TResult>? _options;

    internal QueryCache<TArg, TResult> Cache { get; }

    /// <summary>
    /// Creates a new query endpoint with a given query function. In most cases, the query function
    /// will be a call to an HTTP endpoint, but it can be any async function.
    /// </summary>
    public Endpoint(
        Func<TArg, CancellationToken, Task<TResult>> queryFn,
        EndpointOptions<TArg, TResult>? options = null)
    {
        _options = options ?? EndpointOptions<TArg, TResult>.Default;
        Cache = new(queryFn, _options);
    }

    /// <summary>
    /// Creates a new query endpoint with a given query function. In most cases, the query function
    /// will be a call to an HTTP endpoint, but it can be any async function.
    /// </summary>
    public Endpoint(
        Func<TArg, Task<TResult>> queryFn,
        EndpointOptions<TArg, TResult>? options = null)
        : this((arg, _) => queryFn(arg), options)
    { }

    /// <summary>
    /// Creates a new <see cref="Query{TArg, TResult}"/> object, which can be used to make queries
    /// to this endpoint.
    /// </summary>
    /// <returns>A new <see cref="Query{TArg, TResult}"/> object which shares the same cache as other queries from this endpoint.</returns>
    /// <param name="options">Additional options to use when querying</param>
    public Query<TArg, TResult> Use(QueryOptions<TArg, TResult>? options = null)
    {
        return new Query<TArg, TResult>(Cache, options);
    }

    /// <summary>
    /// Invalidates all cached return values from this endpoint. Any components using them will
    /// automatically re-fetch their data.
    /// </summary>
    public void InvalidateAll()
    {
        Cache.InvalidateAll();
    }

    /// <summary>
    /// Invalidates a specific value in the cache, based on its query argument.
    /// </summary>
    /// <param name="arg">The query argument to invalidate</param>
    /// <remarks>
    /// <para/>
    /// This should be preferred over <see cref="InvalidateWhere"/>, because it is more efficient.
    /// <para/>
    /// If no queries are using the provided query argument, this does nothing.
    /// </remarks>
    public void Invalidate(TArg arg)
    {
        Cache.Invalidate(arg);
    }

    /// <summary>
    /// Invalidates all cache entries that match the given predicate.
    /// </summary>
    /// <param name="predicate">
    /// The function to use when deciding which entries to invalidate. The arguments to this
    /// function are the query arg, and the query object itself. This should return <c>true</c> for
    /// entries that should be invalidated, or false otherwise.
    /// </param>
    public void InvalidateWhere(Func<TArg, FixedQuery<TArg, TResult>, bool> predicate)
    {
        Cache.InvalidateWhere(predicate);
    }

    /// <inheritdoc cref="QueryCache{TArg, TResult}.UpdateQueryData(TArg, TResult)"/>
    public bool UpdateQueryData(TArg arg, TResult resultData) => Cache.UpdateQueryData(arg, resultData);

    /// <summary>
    /// Begins running the query in the background for the specified query argument, so that the
    /// result can be cached and used immediately when it is needed.
    /// <para/>
    /// If the specified query argument already exists in the cache and was not an error, this does nothing.
    /// </summary>
    public async Task PrefetchAsync(TArg arg)
    {
        var query = Cache.GetOrAdd(arg);
        if (query.Status == QueryStatus.Idle || query.Status == QueryStatus.Error)
        {
            await query.RefetchAsync();
        }
    }

    /// <summary>
    /// Runs the original query function once, completely bypassing caching and other extra behaviour
    /// </summary>
    /// <param name="arg">The argument passed to the query function</param>
    /// <param name="ct">An optional cancellation token</param>
    /// <returns>The value returned by the query function</returns>
    public Task<TResult> Invoke(TArg arg, CancellationToken ct = default)
    {
        return Cache.QueryFn.Invoke(arg, ct);
    }
}

/// <summary>
/// An alternate version of <see cref="Endpoint{TArg, TResult}"/> for queries that have no parameters.
/// </summary>
public sealed class ParameterlessEndpoint<TResult> : Endpoint<Unit, TResult>
{
    /// <summary>
    /// Creates a new Endpoint from a query function with no parameters.
    /// </summary>
    public ParameterlessEndpoint(
        Func<CancellationToken, Task<TResult>> queryFn,
        EndpointOptions<Unit, TResult>? options = null
    ) : base((_, ct) => queryFn(ct), options)
    { }

    /// <summary>
    /// Creates a new Endpoint from a query function with no parameters and no CancellationToken.
    /// </summary>
    public ParameterlessEndpoint(
        Func<Task<TResult>> queryFn,
        EndpointOptions<Unit, TResult>? options = null
    ) : base((_, _) => queryFn(), options)
    { }

    /// <inheritdoc cref="Endpoint{TArg, TResult}.Use"/>
    public new Query<TResult> Use(QueryOptions<Unit, TResult>? options = null) =>
        new(Cache, options);
}

/// <summary>
/// An alternate version of <see cref="Endpoint{TArg, TResult}"/> for queries that have no return value.
/// </summary>
public sealed class MutationEndpoint<TArg> : Endpoint<TArg, Unit>
{
    /// <summary>
    /// Creates a new Endpoint from a query function with no return value.
    /// </summary>
    public MutationEndpoint(
        Func<TArg, CancellationToken, Task> queryFn,
        EndpointOptions<TArg, Unit>? options = null
    ) : base(
        async (arg, token) =>
        {
            await queryFn(arg, token);
            return default;
        },
        options
    )
    { }

    /// <summary>
    /// Creates a new Endpoint from a query function with no return value and no CancellationToken.
    /// </summary>
    public MutationEndpoint(
        Func<TArg, Task> queryFn,
        EndpointOptions<TArg, Unit>? options = null
    ) : base(
        async (arg, _) =>
        {
            await queryFn(arg);
            return default;
        },
        options
    )
    { }

    /// <inheritdoc cref="Endpoint{TArg, TResult}.Use"/>
    public new Mutation<TArg> Use(QueryOptions<TArg, Unit>? options = null) =>
        new(Cache, options);
}
