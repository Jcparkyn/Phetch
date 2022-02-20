﻿namespace Fetcher;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

public class Mutation<TArg, TResult>
{
    private readonly Func<TArg, Task<TResult>> _action;
    private readonly Action? _onError;

    private Task<TResult>? _lastActionCall;

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

    public Mutation(
        Action? onStateChanged,
        Func<TArg, Task<TResult>> action,
        Action? onError = null)
    {
        _action = action;
        _onError = onError;
        OnStateChanged = onStateChanged;
    }

    public void Trigger(TArg arg) => _ = TriggerAsync(arg);

    public async Task<TResult> TriggerAsync(TArg arg)
    {
        Status = QueryStatus.Loading;
        Error = null;

        OnStateChanged?.Invoke(); // TODO: Avoid unnecessary re-renders

        var thisActionCall = _action(arg);
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
}
