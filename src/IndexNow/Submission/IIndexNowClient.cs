namespace IndexNow.Submission;

internal interface IIndexNowClient
{
    /// <returns>Whether the endpoint accepted the batch.</returns>
    Task<bool> SubmitAsync(IndexNowBatch batch, CancellationToken cancellationToken);
}
