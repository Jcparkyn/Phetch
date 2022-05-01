﻿namespace Phetch.Blazor;

using Microsoft.AspNetCore.Components;

public partial class UseQueryEndpoint<TArg, TResult>
{
    private Query<TArg, TResult>? query;
    private QueryEndpoint<TArg, TResult>? endpoint { get; set; }

    [Parameter, EditorRequired]
    public QueryEndpoint<TArg, TResult>? Endpoint
    {
        get => endpoint;
        set
        {
            if (ReferenceEquals(endpoint, value))
                return;
            TryUnsubscribe(query);
            if (value is not null)
            {
                query = GetQuery(value, options);
                endpoint = value;
            }
        }
    }

    [Parameter, EditorRequired]
    public RenderFragment<Query<TArg, TResult>> ChildContent { get; set; } = null !;
    // Other parameters can be set before Param is set, so don't use Param until it has been set.
    // A nullable here would not work for queries where null is a valid argument.
    private bool hasSetParam = false;
    private TArg param = default!;

    [Parameter, EditorRequired]
    public TArg Param
    {
        get => param;
        set
        {
            hasSetParam = true;
            query?.SetParam(value);
        }
    }

    private QueryOptions<TResult>? options;
    [Parameter]
    public QueryOptions<TResult>? Options
    {
        get => options;
        set
        {
            if (options == value)
                return;
            TryUnsubscribe(query);
            if (endpoint is not null)
            {
                query = GetQuery(endpoint, value);
            }

            options = value;
        }
    }

    public void Dispose()
    {
        TryUnsubscribe(query);
    }

    private Query<TArg, TResult> GetQuery(QueryEndpoint<TArg, TResult> endpoint, QueryOptions<TResult>? options)
    {
        var newQuery = options is null ? endpoint.Use() : endpoint.Use(options);
        newQuery.StateChanged += StateHasChanged;
        if (hasSetParam)
            newQuery.SetParam(param);
        return newQuery;
    }

    private void TryUnsubscribe(Query<TArg, TResult>? query)
    {
        if (query is not null)
        {
            query.StateChanged -= StateHasChanged;
            query.Detach();
        }
    }
}
