using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

// Public demo version: configuration via environment variables / arguments, no hard-coded secrets.
// Required ENV variables (or defaults in dev):
//  DEMO_TENANT (default: test)
//  DEMO_USERNAME (default: admin@abp.io)
//  DEMO_PASSWORD (default: 123456)
//  DEMO_CLIENT_ID (default: Adzup_App)
//  DEMO_AUTH_BASE (default: https://localhost:44332)
//  DEMO_API_BASE (default: https://localhost:44389)
//  DEMO_POP_BASE (default: https://localhost:7038)
// Usage: dotnet run [optionalPathToMediaFile]
// NOTE: This sample is for educational purposes – do NOT commit real credentials.

record CreatePlaylistRequest(string name, decimal? shareOfVoice, string? description);
record CreateOrUpdatePlaylistFilesRequest(List<Guid> fileIds);
record CreateOrUpdatePlaylistScreensRequest(List<Guid> screenIds);
record UpdatePlaylistCalendarItem(DateTime startDate, DateTime endDate, bool isAllDay, string? recurrenceRule);
record PlaylistDto(Guid Id, string Name);

internal static class Program
{
    private static async Task<int> Main()
    {
        var tenant = Env("DEMO_TENANT", "test");
        var username = Env("DEMO_USERNAME", "admin@abp.io");
        var password = Env("DEMO_PASSWORD", "User1029#");
        var clientId = Env("DEMO_CLIENT_ID", "Adzup_App");
        var authBase = Env("DEMO_AUTH_BASE", "https://localhost:44332");
        var apiBase = Env("DEMO_API_BASE", "https://localhost:44389");
        var popBase = Env("DEMO_POP_BASE", "https://localhost:7038");

        Console.WriteLine("--- Public Playlist Publish Demo ---\n");
        var imagePath = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault() ?? "sample.jpg";
        if (!File.Exists(imagePath))
        {
            Console.WriteLine($"Image file not found: {imagePath}. Provide a path or place sample.jpg in working dir.");
            return 1;
        }

        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };

        using var authClient = new HttpClient(handler) { BaseAddress = new Uri(authBase) };
        using var apiClient = new HttpClient(handler) { BaseAddress = new Uri(apiBase) };
        using var popClient = new HttpClient(handler) { BaseAddress = new Uri(popBase) };

        // 1. Token
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = username,
            ["password"] = password,
            ["client_id"] = clientId,
            ["scope"] = "offline_access openid profile email roles Adzup PopManagement"
        });
        form.Headers.Add("__tenant", tenant);
        var tokenResp = await authClient.PostAsync("connect/token", form);
        if(!tokenResp.IsSuccessStatusCode)
        {
            Console.WriteLine("Token request failed: " + (int)tokenResp.StatusCode);
            Console.WriteLine(await tokenResp.Content.ReadAsStringAsync());
            return 1;
        }
        var tokenJson = JsonDocument.Parse(await tokenResp.Content.ReadAsStringAsync());
        var accessToken = tokenJson.RootElement.GetProperty("access_token").GetString()!;
        apiClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        apiClient.DefaultRequestHeaders.Add("__tenant", tenant);
        popClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        popClient.DefaultRequestHeaders.Add("__tenant", tenant);
        Console.WriteLine("Token acquired.\n");

        // 2. File reuse or upload
        var fileName = Path.GetFileName(imagePath);
        var fileId = await TryFindFileAsync(apiClient, fileName) ?? await UploadAsync(apiClient, imagePath, fileName);
        if(fileId == Guid.Empty)
        {
            Console.WriteLine("File acquisition failed.");
            return 1;
        }

        // 3. Idempotent playlist deletion
        const string playlistName = "Demo Playlist";
        var existingPlaylistId = await TryFindPlaylistByNameAsync(apiClient, playlistName);
        if(existingPlaylistId.HasValue)
        {
            Console.WriteLine($"Playlist '{playlistName}' exists. Cleaning up...");
            await DetachAndDeleteAsync(apiClient, existingPlaylistId.Value);
        }

        // 4. Create playlist
        var createReq = new CreatePlaylistRequest(playlistName, 1.0m, "Created via public demo");
        var playlistResp = await apiClient.PostAsJsonAsync("api/playlist-management/playlist", createReq);
        if(!playlistResp.IsSuccessStatusCode)
        {
            Console.WriteLine("Create playlist failed: " + await playlistResp.Content.ReadAsStringAsync());
            return 1;
        }
        var playlistId = JsonDocument.Parse(await playlistResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        Console.WriteLine($"Playlist created: {playlistId}\n");

        // 5. Attach file
        var attachFilePayload = new CreateOrUpdatePlaylistFilesRequest(new List<Guid>{ fileId });
        var attachFileResp = await apiClient.PutAsJsonAsync($"api/playlist-management/playlist-file/create-or-update-batch/{playlistId}", attachFilePayload);
        Console.WriteLine(attachFileResp.IsSuccessStatusCode ? "File attached" : "File attach failed: " + await attachFileResp.Content.ReadAsStringAsync());

        // 6. Screen selection
        var screenList = await apiClient.GetAsync("api/inventory-management/screen?skipCount=0&maxResultCount=1");
        if(!screenList.IsSuccessStatusCode)
        {
            Console.WriteLine("Screen list failed: " + (int)screenList.StatusCode);
            return 1;
        }
        var screenDoc = JsonDocument.Parse(await screenList.Content.ReadAsStringAsync());
        if(!screenDoc.RootElement.TryGetProperty("items", out var scrItems) || scrItems.GetArrayLength()==0)
        {
            Console.WriteLine("No screens returned.");
            return 1;
        }
        var screenId = scrItems[0].GetProperty("id").GetGuid();
        Console.WriteLine($"Using ScreenId: {screenId}");

        var attachScreenPayload = new CreateOrUpdatePlaylistScreensRequest(new List<Guid>{ screenId });
        var attachScreenResp = await apiClient.PutAsJsonAsync($"api/playlist-management/playlist-screen/create-or-update-batch/{playlistId}", attachScreenPayload);
        Console.WriteLine(attachScreenResp.IsSuccessStatusCode ? "Screen attached" : "Screen attach failed: " + await attachScreenResp.Content.ReadAsStringAsync());

        // 7. Calendar
        var startUtc = DateTime.UtcNow;
        var endUtc = startUtc.AddDays(7);
        var calPayload = new[]{ new { startDate = startUtc, endDate = endUtc, isAllDay = false, recurrenceRule = (string?)null } };
        var calResp = await apiClient.PutAsJsonAsync($"api/playlist-management/playlist/{playlistId}/update-calendar", calPayload);
        Console.WriteLine(calResp.IsSuccessStatusCode ? "Calendar updated" : "Calendar update failed: " + await calResp.Content.ReadAsStringAsync());

        // 8. URL item
        var urlPayload = new { name = "Demo URL", value = "https://example.com", duration = 30 };
        var urlResp = await apiClient.PostAsJsonAsync($"api/playlist-management/playlist-url?playlistId={playlistId}", urlPayload);
        Console.WriteLine(urlResp.IsSuccessStatusCode ? "URL added" : "URL add failed: " + await urlResp.Content.ReadAsStringAsync());

        // 9. Publish
        var publishResp = await apiClient.PostAsync($"api/playlist-management/playlist/{playlistId}/publish", null);
        Console.WriteLine(publishResp.IsSuccessStatusCode ? "Publish triggered" : "Publish failed: " + await publishResp.Content.ReadAsStringAsync());

        // 10. POP overview
        Console.WriteLine("Waiting 5s before POP overview...");
        await Task.Delay(TimeSpan.FromSeconds(5));
        var now = DateTime.UtcNow;
        var popUrl = $"api/PopManagement/pop/get-overview-by-screen-id?ScreenId={screenId}&Year={now.Year}&Month={now.Month}";
        var popResp = await popClient.GetAsync(popUrl);
        Console.WriteLine(popResp.IsSuccessStatusCode ? "POP Overview:\n" + await popResp.Content.ReadAsStringAsync() : "POP overview failed: " + await popResp.Content.ReadAsStringAsync());

        return 0;
    }

    private static string Env(string key, string fallback) => Environment.GetEnvironmentVariable(key) ?? fallback;

    private static async Task<Guid?> TryFindFileAsync(HttpClient apiClient, string fileName)
    {
        try
        {
            var resp = await apiClient.GetAsync("api/file-management/file-descriptor");
            if(!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if(doc.RootElement.TryGetProperty("items", out var items))
            {
                foreach(var el in items.EnumerateArray())
                {
                    if(el.TryGetProperty("name", out var nameProp) && string.Equals(nameProp.GetString(), fileName, StringComparison.OrdinalIgnoreCase) && el.TryGetProperty("id", out var idProp) && Guid.TryParse(idProp.GetString(), out var gid))
                        return gid;
                }
            }
        }
        catch { /* swallow for demo */ }
        return null;
    }

    private static async Task<Guid> UploadAsync(HttpClient apiClient, string path, string fileName)
    {
        // pre-upload info
        var pre = await apiClient.PostAsJsonAsync("api/file-management/file-descriptor/creative-pre-upload-info", new[]{ new { fileName } });
        if(!pre.IsSuccessStatusCode)
        {
            Console.WriteLine("Pre-upload info failed: " + await pre.Content.ReadAsStringAsync());
            return Guid.Empty;
        }
        var bytes = await File.ReadAllBytesAsync(path);
        var content = new MultipartFormDataContent();
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new MediaTypeHeaderValue(Mime(fileName));
        content.Add(part, "File", fileName);
        var upload = await apiClient.PostAsync($"api/file-management/file-descriptor/upload?Name={Uri.EscapeDataString(fileName)}", content);
        if(!upload.IsSuccessStatusCode)
        {
            Console.WriteLine("Upload failed: " + await upload.Content.ReadAsStringAsync());
            return Guid.Empty;
        }
        using var doc = JsonDocument.Parse(await upload.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<Guid?> TryFindPlaylistByNameAsync(HttpClient apiClient, string name)
    {
        try
        {
            var resp = await apiClient.GetAsync($"api/playlist-management/playlist?Filter={Uri.EscapeDataString(name)}&SkipCount=0&MaxResultCount=10");
            if(!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if(doc.RootElement.TryGetProperty("items", out var items))
            {
                foreach(var el in items.EnumerateArray())
                {
                    if(el.TryGetProperty("name", out var nProp) && string.Equals(nProp.GetString(), name, StringComparison.OrdinalIgnoreCase) && el.TryGetProperty("id", out var idProp) && Guid.TryParse(idProp.GetString(), out var gid))
                        return gid;
                }
            }
        }
        catch { }
        return null;
    }

    private static async Task DetachAndDeleteAsync(HttpClient apiClient, Guid playlistId)
    {
        async Task Detach(string type)
        {
            var listUrl = type == "file" ? $"api/playlist-management/playlist-file?playlistId={playlistId}" : $"api/playlist-management/playlist-screen?playlistId={playlistId}";
            var resp = await apiClient.GetAsync(listUrl);
            if(!resp.IsSuccessStatusCode) return;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if(!doc.RootElement.TryGetProperty("items", out var items) || items.GetArrayLength()==0) return;
            var ids = new List<Guid>();
            foreach(var el in items.EnumerateArray())
                if(el.TryGetProperty("id", out var idProp) && Guid.TryParse(idProp.GetString(), out var gid)) ids.Add(gid);
            if(ids.Count==0) return;
            var delReq = new HttpRequestMessage(HttpMethod.Delete, type == "file" ? "api/playlist-management/playlist-file/delete-batch" : "api/playlist-management/playlist-screen/delete-batch")
            {
                Content = JsonContent.Create(new { ids })
            };
            await apiClient.SendAsync(delReq);
        }
        await Detach("file");
        await Detach("screen");
        await apiClient.DeleteAsync($"api/playlist-management/playlist/{playlistId}");
    }

    private static string Mime(string fileName) => fileName.ToLowerInvariant() switch
    {
        var n when n.EndsWith(".jpg") || n.EndsWith(".jpeg") => "image/jpeg",
        var n when n.EndsWith(".png") => "image/png",
        var n when n.EndsWith(".gif") => "image/gif",
        var n when n.EndsWith(".mp4") => "video/mp4",
        _ => "application/octet-stream"
    };
}
