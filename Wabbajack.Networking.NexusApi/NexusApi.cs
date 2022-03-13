using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Wabbajack.Common;
using Wabbajack.DTOs;
using Wabbajack.DTOs.Logins;
using Wabbajack.Networking.Http;
using Wabbajack.Networking.Http.Interfaces;
using Wabbajack.Networking.NexusApi.DTOs;
using Wabbajack.Paths;
using Wabbajack.Paths.IO;
using Wabbajack.RateLimiter;
using Wabbajack.Server.DTOs;

namespace Wabbajack.Networking.NexusApi;

public class NexusApi
{
    private readonly ApplicationInfo _appInfo;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly IResource<HttpClient> _limiter;
    private readonly ILogger<NexusApi> _logger;
    protected readonly ITokenProvider<NexusApiState> ApiKey;

    public NexusApi(ITokenProvider<NexusApiState> apiKey, ILogger<NexusApi> logger, HttpClient client,
        IResource<HttpClient> limiter,
        ApplicationInfo appInfo, JsonSerializerOptions jsonOptions)
    {
        ApiKey = apiKey;
        _logger = logger;
        _client = client;
        _appInfo = appInfo;
        _jsonOptions = jsonOptions;
        _limiter = limiter;
    }

    public virtual async Task<(ValidateInfo info, ResponseMetadata header)> Validate(
        CancellationToken token = default)
    {
        var msg = await GenerateMessage(HttpMethod.Get, Endpoints.Validate);
        return await Send<ValidateInfo>(msg, token);
    }

    public virtual async Task<(ModInfo info, ResponseMetadata header)> ModInfo(string nexusGameName, long modId,
        CancellationToken token = default)
    {
        var msg = await GenerateMessage(HttpMethod.Get, Endpoints.ModInfo, nexusGameName, modId);
        return await Send<ModInfo>(msg, token);
    }

    public virtual async Task<(ModFiles info, ResponseMetadata header)> ModFiles(string nexusGameName, long modId,
        CancellationToken token = default)
    {
        var msg = await GenerateMessage(HttpMethod.Get, Endpoints.ModFiles, nexusGameName, modId);
        return await Send<ModFiles>(msg, token);
    }

    public virtual async Task<(ModFile info, ResponseMetadata header)> FileInfo(string nexusGameName, long modId,
        long fileId, CancellationToken token = default)
    {
        var msg = await GenerateMessage(HttpMethod.Get, Endpoints.ModFile, nexusGameName, modId, fileId);
        return await Send<ModFile>(msg, token);
    }

    public virtual async Task<(DownloadLink[] info, ResponseMetadata header)> DownloadLink(string nexusGameName,
        long modId, long fileId, CancellationToken token = default)
    {
        var msg = await GenerateMessage(HttpMethod.Get, Endpoints.DownloadLink, nexusGameName, modId, fileId);
        return await Send<DownloadLink[]>(msg, token);
    }

    protected virtual async Task<(T data, ResponseMetadata header)> Send<T>(HttpRequestMessage msg,
        CancellationToken token = default)
    {
        using var job = await _limiter.Begin($"API call to the Nexus {msg.RequestUri!.PathAndQuery}", 0, token);

        using var result = await _client.SendAsync(msg, token);
        if (!result.IsSuccessStatusCode)
            throw new HttpException(result);

        var headers = ParseHeaders(result);
        job.Size = result.Content.Headers.ContentLength ?? 0;
        await job.Report((int) (result.Content.Headers.ContentLength ?? 0), token);

        var body = await result.Content.ReadAsByteArrayAsync(token);
        return (JsonSerializer.Deserialize<T>(body, _jsonOptions)!, headers);
    }

    protected virtual ResponseMetadata ParseHeaders(HttpResponseMessage result)
    {
        var metaData = new ResponseMetadata();

        {
            if (result.Headers.TryGetValues("x-rl-daily-limit", out var limits))
                if (int.TryParse(limits.First(), out var limit))
                    metaData.DailyLimit = limit;
        }

        {
            if (result.Headers.TryGetValues("x-rl-daily-remaining", out var limits))
                if (int.TryParse(limits.First(), out var limit))
                    metaData.DailyRemaining = limit;
        }

        {
            if (result.Headers.TryGetValues("x-rl-daily-reset", out var resets))
                if (DateTime.TryParse(resets.First(), out var reset))
                    metaData.DailyReset = reset;
        }

        {
            if (result.Headers.TryGetValues("x-rl-hourly-limit", out var limits))
                if (int.TryParse(limits.First(), out var limit))
                    metaData.HourlyLimit = limit;
        }

        {
            if (result.Headers.TryGetValues("x-rl-hourly-remaining", out var limits))
                if (int.TryParse(limits.First(), out var limit))
                    metaData.HourlyRemaining = limit;
        }

        {
            if (result.Headers.TryGetValues("x-rl-hourly-reset", out var resets))
                if (DateTime.TryParse(resets.First(), out var reset))
                    metaData.HourlyReset = reset;
        }


        {
            if (result.Headers.TryGetValues("x-runtime", out var runtimes))
                if (double.TryParse(runtimes.First(), out var reset))
                    metaData.Runtime = reset;
        }

        _logger.LogInformation("Nexus API call finished: {Runtime} - Remaining Limit: {RemainingLimit}",
            metaData.Runtime, Math.Max(metaData.DailyRemaining, metaData.HourlyRemaining));

        return metaData;
    }

    protected virtual async ValueTask<HttpRequestMessage> GenerateMessage(HttpMethod method, string uri,
        params object?[] parameters)
    {
        var msg = new HttpRequestMessage();
        msg.Method = method;

        var userAgent =
            $"{_appInfo.ApplicationSlug}/{_appInfo.Version} ({_appInfo.OSVersion}; {_appInfo.Platform})";

        msg.RequestUri = new Uri($"https://api.nexusmods.com/{string.Format(uri, parameters)}");
        msg.Headers.Add("User-Agent", userAgent);
        msg.Headers.Add("Application-Name", _appInfo.ApplicationSlug);
        msg.Headers.Add("Application-Version", _appInfo.Version);
        msg.Headers.Add("apikey", (await ApiKey.Get())!.ApiKey);
        msg.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return msg;
    }

    public async Task<(UpdateEntry[], ResponseMetadata headers)> GetUpdates(Game game, CancellationToken token)
    {
        var msg = await GenerateMessage(HttpMethod.Get, Endpoints.Updates, game.MetaData().NexusName, "1m");
        return await Send<UpdateEntry[]>(msg, token);
    }

    public async Task<ChunkStatus> ChunkStatus(UploadDefinition definition, Chunk chunk)
    {
        var msg = new HttpRequestMessage();
        msg.Method = HttpMethod.Get;

        var query =
            $"resumableChunkNumber={chunk.Index + 1}&resumableCurrentChunkSize={chunk.Size}&resumableTotalSize={definition.FileSize}"
            + $"&resumableType=&resumableIdentifier={definition.ResumableIdentifier}&resumableFilename={definition.ResumableRelativePath}"
            + $"&resumableRelativePath={definition.ResumableRelativePath}&resumableTotalChunks={definition.Chunks().Count()}";

        msg.RequestUri = new Uri($"https://upload.nexusmods.com/uploads/chunk?{query}");

        using var result = await _client.SendAsync(msg);
        if (!result.IsSuccessStatusCode)
            throw new HttpException(result);
        if (result.StatusCode == HttpStatusCode.NoContent)
            return DTOs.ChunkStatus.NoContent;

        var status = await result.Content.ReadFromJsonAsync<ChunkStatusResult>();
        return status?.Status ?? false ? DTOs.ChunkStatus.Done : DTOs.ChunkStatus.Waiting;
    }

    public async Task<ChunkStatusResult> UploadChunk(UploadDefinition d, Chunk chunk)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent((chunk.Index+1).ToString()), "resumableChunkNumber");
        form.Add(new StringContent(UploadDefinition.ChunkSize.ToString()), "resumableChunkSize");
        form.Add(new StringContent(chunk.Size.ToString()), "resumableCurrentChunkSize");
        form.Add(new StringContent(d.FileSize.ToString()), "resumableTotalSize");
        form.Add(new StringContent(""), "resumableType");
        form.Add(new StringContent(d.ResumableIdentifier), "resumableIdentifier");
        form.Add(new StringContent(d.ResumableRelativePath), "resumableFilename");
        form.Add(new StringContent(d.ResumableRelativePath), "resumableRelativePath");
        form.Add(new StringContent(d.Chunks().Count().ToString()), "resumableTotalChunks");

        await using var ms = new MemoryStream();
        await using var fs = d.Path.Open(FileMode.Open, FileAccess.Read, FileShare.Read);
        fs.Position = chunk.Offset;
        await fs.CopyToLimitAsync(ms, (int)chunk.Size, CancellationToken.None);
        ms.Position = 0;
        
        form.Add(new StreamContent(ms), "file", "blob");

        var msg = new HttpRequestMessage(HttpMethod.Post,  "https://upload.nexusmods.com/uploads/chunk");
        msg.Content = form;

        var result = await _client.SendAsync(msg);
        if (result.StatusCode != HttpStatusCode.OK)
            throw new HttpException(result);

        var response = await result.Content.ReadFromJsonAsync<ChunkStatusResult>(_jsonOptions);
        return response;
    }
    public async Task UploadFile(UploadDefinition d)
    {
        _logger.LogInformation("Checking Access");
        await CheckAccess();
        
        _logger.LogInformation("Checking chunk status");
        
        var numberOfChunks = d.Chunks().Count();
        var chunkStatus = new ChunkStatusResult();
        foreach (var chunk in d.Chunks())
        {
            var status = await ChunkStatus(d, chunk);
            _logger.LogInformation("({Index}/{MaxChunks}) Chunk status: {Status}", chunk.Index, numberOfChunks, status);
            if (status == DTOs.ChunkStatus.NoContent)
            {
                _logger.LogInformation("({Index}/{MaxChunks}) Uploading", chunk.Index, numberOfChunks);
                chunkStatus = await UploadChunk(d, chunk);
            }
        }

        await WaitForFileStatus(chunkStatus);

        await AddFile(d, chunkStatus);

    }

    private async Task CheckAccess()
    {
        var msg = new HttpRequestMessage(HttpMethod.Get, "https://www.nexusmods.com/users/myaccount");
        msg.AddCookies((await ApiKey.Get())!.Cookies);
        using var response = await _client.SendAsync(msg);
        var body = await response.Content.ReadAsStringAsync();

        if (body.Contains("You are not allowed to access this area!"))
            throw new HttpException(403, "Nexus Cookies are incorrect");
    }

    private async Task AddFile(UploadDefinition d, ChunkStatusResult status)
    {
        _logger.LogInformation("Saving file update {Name} to {Game}:{ModId}", d.Path.FileName, d.Game, d.ModId);
        
        var msg = new HttpRequestMessage(HttpMethod.Post,
            "https://www.nexusmods.com/Core/Libs/Common/Managers/Mods?AddFile");
        msg.Headers.Referrer =
            new Uri(
                $"https://www.nexusmods.com/{d.Game.MetaData().NexusName}/mods/edit/?id={d.ModId}&game_id={d.GameId}&step=files");
        
        msg.AddCookies((await ApiKey.Get())!.Cookies);
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(d.GameId.ToString()), "game_id");
        form.Add(new StringContent(d.Name), "name");
        form.Add(new StringContent(d.Version), "file-version");
        form.Add(new StringContent((d.RemoveOldVersion ? 1 : 0).ToString()), "update-version");
        form.Add(new StringContent(((int)Enum.Parse<Category>(d.Category, true)).ToString()), "category");
        form.Add(new StringContent((d.NewExisting ? 1 : 0).ToString()), "new-existing");
        form.Add(new StringContent(d.OldFileId.ToString()), "old_file_id");
        form.Add(new StringContent((d.RemoveOldVersion ? 1 : 0).ToString()), "remove-old-version");
        form.Add(new StringContent(d.BriefOverview), "brief-overview");
        form.Add(new StringContent((d.SetAsMain ? 1 : 0).ToString()), "set_as_main_nmm");
        form.Add(new StringContent(status.UUID), "file_uuid");
        form.Add(new StringContent(d.FileSize.ToString()), "file_size");
        form.Add(new StringContent(d.ModId.ToString()), "mod_id");
        form.Add(new StringContent(d.ModId.ToString()), "id");
        form.Add(new StringContent("save"), "action");
        form.Add(new StringContent(status.Filename), "uploaded_file");
        form.Add(new StringContent(d.Path.FileName.ToString()), "original_file");
        msg.Content = form;
        
        using var result = await _client.SendAsync(msg);
        if (!result.IsSuccessStatusCode)
            throw new HttpException(result);
    }

    private async Task<FileStatusResult> WaitForFileStatus(ChunkStatusResult chunkStatus)
    {
        while (true)
        {
            _logger.LogInformation("Checking file status of {Uuid}", chunkStatus.UUID);
            var data = await _client.GetFromJsonAsync<FileStatusResult>(
                $"https://upload.nexusmods.com/uploads/check_status?id={chunkStatus.UUID}");
            if (data.FileChunksAssembled)
                return data;
            await Task.Delay(TimeSpan.FromSeconds(5));
        }
    }
}