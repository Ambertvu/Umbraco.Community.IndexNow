namespace IndexNow.Queue;

/// <summary>Add something to the next submission yourself, such as a page Umbraco does not know about.</summary>
public interface IIndexNowQueue
{
    /// <summary>Submits the current URLs of a content item.</summary>
    void EnqueueContent(Guid contentKey);

    /// <summary>Submits an absolute URL. It is checked against the host and path settings like any other.</summary>
    void EnqueueUrl(string absoluteUrl);
}
