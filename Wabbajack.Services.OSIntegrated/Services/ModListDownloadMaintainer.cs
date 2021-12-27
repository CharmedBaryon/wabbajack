using System;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Wabbajack.Common;
using Wabbajack.Downloaders;
using Wabbajack.DTOs;
using Wabbajack.Paths;
using Wabbajack.Paths.IO;
using Wabbajack.RateLimiter;
using Wabbajack.VFS;

namespace Wabbajack.Services.OSIntegrated.Services;

public class ModListDownloadMaintainer
{
    private readonly ILogger<ModListDownloadMaintainer> _logger;
    private readonly Configuration _configuration;
    private readonly DownloadDispatcher _dispatcher;
    private readonly FileHashCache _hashCache;
    private readonly IResource<DownloadDispatcher> _rateLimiter;

    public ModListDownloadMaintainer(ILogger<ModListDownloadMaintainer> logger, Configuration configuration,
        DownloadDispatcher dispatcher, FileHashCache hashCache, IResource<DownloadDispatcher> rateLimiter)
    {
        _logger = logger;
        _configuration = configuration;
        _dispatcher = dispatcher;
        _hashCache = hashCache;
        _rateLimiter = rateLimiter;
    }

    public AbsolutePath ModListPath(ModlistMetadata metadata)
    {
        return _configuration.ModListsDownloadLocation.Combine(metadata.Links.MachineURL).WithExtension(Ext.Wabbajack);
    }

    public async Task<bool> HaveModList(ModlistMetadata metadata, CancellationToken? token = null)
    {
        token ??= CancellationToken.None;
        var path = ModListPath(metadata);
        if (!path.FileExists()) return false;

        return await _hashCache.FileHashCachedAsync(path, token.Value) == metadata.DownloadMetadata!.Hash;
    }

    public (IObservable<Percent> Progress, Task Task) DownloadModlist(ModlistMetadata metadata, CancellationToken? token = null)
    {
        var path = ModListPath(metadata);
        
        token ??= CancellationToken.None;
        
        var progress = new Subject<Percent>();
        progress.OnNext(Percent.Zero);

        var tsk = Task.Run(async () =>
        {
            var job = await _rateLimiter.Begin($"Downloading {metadata.Title}", metadata.DownloadMetadata!.Size, token.Value);

            job.OnUpdate += (_, pr) =>
            {
                progress.OnNext(pr.Progress);
            };

           var hash = await _dispatcher.Download(new Archive()
            {
                State = _dispatcher.Parse(new Uri(metadata.Links.Download))!,
                Size = metadata.DownloadMetadata.Size,
                Hash = metadata.DownloadMetadata.Hash
            }, path, job, token.Value);
           
           _hashCache.FileHashWriteCache(path, hash);
        });

        return (progress, tsk);
    }
}