namespace IndexNow.Filtering;

/// <summary>
/// Decides whether a URL may be submitted. Every registered filter must agree, so a filter of your own only adds
/// exclusions to the built-in rules. Register it with <c>builder.AddIndexNowUrlFilter&lt;T&gt;()</c>.
/// </summary>
public interface IIndexNowUrlFilter
{
    ValueTask<bool> ShouldSubmitAsync(IndexNowCandidate candidate, CancellationToken cancellationToken);
}
