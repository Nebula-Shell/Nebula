using CaelestiaWin.Core.Interfaces;
using CaelestiaWin.Core.Models;

namespace CaelestiaWin.Platform.Windows.Services;

public sealed class MockMediaService : IMediaService
{
    public MediaSessionModel CurrentSession { get; } = new();

    public void Start()
    {
    }

    public void Stop()
    {
    }

    public Task PlayPauseAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task NextAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task PreviousAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
