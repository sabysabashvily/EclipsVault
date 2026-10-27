using System.Text;

namespace EclipsVault.IntegrationTests;

/// <summary>Holds a form at antiforgery's body read, after authentication has finished.</summary>
internal sealed class GatedRequestBody(string form) : MemoryStream(Encoding.UTF8.GetBytes(form))
{
    private readonly TaskCompletionSource _readStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task ReadStarted => _readStarted.Task;

    public void Release() => _release.TrySetResult();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        _readStarted.TrySetResult();
        await _release.Task.WaitAsync(cancellationToken);
        return await base.ReadAsync(buffer, cancellationToken);
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        _readStarted.TrySetResult();
        await _release.Task.WaitAsync(cancellationToken);
        return await base.ReadAsync(buffer, offset, count, cancellationToken);
    }
}
