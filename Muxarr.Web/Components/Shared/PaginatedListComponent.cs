using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Caching.Memory;

namespace Muxarr.Web.Components.Shared;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class FilterAttribute : Attribute;

public abstract class PaginatedListComponentBase : AuthStateComponent
{
    private Dictionary<MemberInfo, object?>? _filterProperties;

    [Inject]
    public required IMemoryCache Cache { get; set; }

    [Filter]
    public int Page { get; set; } = 1;

    [Filter]
    public string SearchTerm { get; set; } = string.Empty;

    public int TotalItems { get; set; }
    public int TotalPages { get; set; }

    [Filter]
    public string? CurrentSortProperty { get; set; } = "Id";

    [Filter]
    public bool? IsAscending { get; set; }

    [Filter]
    public int PageSize { get; set; } = 50;

    public bool IsLoading { get; set; }

    // Filter methods / properties
    public string CacheKey { get; set; } = string.Empty;
    public bool HasActiveFilters { get; set; }

    public async Task ChangePageSize(int pageSize)
    {
        PageSize = pageSize;
        await UpdateList();
    }

    public async Task ChangePage(int newPage)
    {
        Page = newPage;
        await UpdateList(false);
    }

    public async Task Search(string term)
    {
        SearchTerm = term;
        await UpdateList();
    }

    public async Task HandleSort(string sortProperty)
    {
        CurrentSortProperty = sortProperty;
        await UpdateList();
    }

    protected virtual async Task UpdateList(bool resetPage = true)
    {
        if (resetPage)
            Page = 1;
        
        IsLoading = true;
        await InvokeStateHasChanged();

        try
        {
            await UpdateListCore();
            SaveFilters();
        }
        finally
        {
            IsLoading = false;
            await InvokeStateHasChanged();
        }
    }

    protected abstract Task UpdateListCore();

    private string GetCacheKey()
    {
        return GetType().Name + CacheKey;
    }

    private void LoadDefaults()
    {
        if (_filterProperties != null)
        {
            return;
        }

        var props = GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(f => f.GetCustomAttribute<FilterAttribute>() != null)
            .Union<MemberInfo>(
                GetType().GetProperties()
                    .Where(p => p.GetCustomAttribute<FilterAttribute>() != null)
            );

        _filterProperties = new Dictionary<MemberInfo, object?>();

        foreach (var property in props)
        {
            _filterProperties[property] = GetValue(property);
        }
    }

    private object? GetValue(MemberInfo member)
    {
        if (member is PropertyInfo property)
        {
            return property.GetValue(this);
        }

        if (member is FieldInfo field)
        {
            return field.GetValue(this);
        }

        return null;
    }

    private void SetValue(MemberInfo member, object? value)
    {
        if (member is PropertyInfo property)
        {
            property.SetValue(this, value);
        }
        else if (member is FieldInfo field)
        {
            field.SetValue(this, value);
        }
    }

    public void SaveFilters()
    {
        // Don't save when defaults are never loaded.
        if (_filterProperties == null)
        {
            return;
        }

        var state = new Dictionary<string, object?>();
        var hasActiveFilters = false;
        foreach (var property in _filterProperties!)
        {
            var value = GetValue(property.Key);
            state[property.Key.Name] = value;

            if (value?.ToString() != property.Value?.ToString())
            {
                hasActiveFilters = true;
            }
        }

        var cacheKey = GetCacheKey();
        Cache.Set(cacheKey, state, TimeSpan.FromMinutes(5));
        HasActiveFilters = hasActiveFilters;
    }

    public async Task LoadFilters(bool reset = false)
    {
        LoadDefaults();
        var cacheKey = GetCacheKey();

        if (reset)
        {
            Cache.Remove(cacheKey);
        }

        var state = Cache.Get<Dictionary<string, object?>>(cacheKey);
        foreach (var (property, defaultValue) in _filterProperties!)
        {
            var currentValue = GetValue(property);

            if (state != null && state.TryGetValue(property.Name, out var value))
            {
                if (currentValue != value)
                {
                    SetValue(property, value);
                }
            }
            else
            {
                if (currentValue != defaultValue)
                {
                    SetValue(property, defaultValue); // The default value.
                }
            }
        }

        await UpdateList(false);
    }
}

public abstract class PaginatedListComponent<T> : PaginatedListComponentBase
{
    protected PaginatedListComponent()
    {
        CacheKey = typeof(T).Name;
    }

    public List<T> Items { get; set; } = new();
}
