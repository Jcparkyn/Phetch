﻿namespace Fetcher;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

public class Query<TArg, TResult>
{
    private readonly Func<TArg, CancellationToken, Task<TResult>> _action;
    private readonly Action? _onError;

    private TArg? _lastArg;
    private Task<TResult>? _lastActionCall;
    private CancellationTokenSource _cts = new();

    public event Action? OnStateChanged;

    public QueryStatus Status { get; private set; } = QueryStatus.Idle;

    public TResult? Data { get; protected set; }

    public Exception? Error { get; protected set; }

    public bool IsLoading => Status == QueryStatus.Loading;

    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsError => Error is not null && Status == QueryStatus.Error;

    [MemberNotNullWhen(true, nameof(Data))]
    public bool IsSuccess => Data is not null && Status == QueryStatus.Success;

    public bool IsUninitialized => Status == QueryStatus.Idle;

    public Query(
        Action? onStateChanged,
        Func<TArg, CancellationToken, Task<TResult>> action,
        Action? onError = null)
    {
        _action = action;
        _onError = onError;
        OnStateChanged = onStateChanged;
    }

    public void Refetch() => _ = RefetchAsync();

    public Task<TResult?> RefetchAsync()
    {
        if (IsUninitialized && typeof(TArg) != typeof(Unit))
        {
            return Task.FromResult(default(TResult)); // TODO throw?
        }
        return SetParamsAsync(_lastArg!, true);
    }

    public void SetParams(TArg arg, bool forceLoad = false) => _ = SetParamsAsync(arg, forceLoad);

    public async Task<TResult?> SetParamsAsync(TArg arg, bool forceLoad = false)
    {
        if (!forceLoad & !IsUninitialized && EqualityComparer<TArg>.Default.Equals(arg, _lastArg!))
        {
            return Data;
        }

        _lastArg = arg;
        Status = QueryStatus.Loading;
        Error = null;

        OnStateChanged?.Invoke(); // TODO: Avoid unnecessary re-renders

        CancelQueriesInProgress();

        var thisActionCall = _action(arg, _cts.Token);
        _lastActionCall = thisActionCall;
        try
        {
            var newData = await thisActionCall;
            // Only update if no new calls have been started since this one started.
            if (thisActionCall == _lastActionCall)
            {
                Status = QueryStatus.Success;
                Data = newData;
                Error = null;
                OnStateChanged?.Invoke();
            }
            return newData;
        }
        catch (TaskCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Only update if no new calls have been started since this one started.
            if (thisActionCall == _lastActionCall)
            {
                Error = ex;
                Status = QueryStatus.Error;
                _onError?.Invoke();
                OnStateChanged?.Invoke();
            }

            throw;
        }
    }

    private void CancelQueriesInProgress()
    {
        if (_lastActionCall is not null && !_lastActionCall.IsCompleted)
        {
            _cts.Cancel();
            _cts = new();
        }
    }
}

public class Query<TResult> : Query<Unit, TResult>
{
    public Query(
        Action? onStateChanged,
        Func<CancellationToken, Task<TResult>> action,
        bool runAutomatically = true
    ) : base(onStateChanged, (_, token) => action(token))
    {
        if (runAutomatically)
        {
            SetParams(default, true); // Trigger an initial query
        }
    }
}