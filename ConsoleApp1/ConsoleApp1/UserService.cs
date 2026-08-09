//internal class ArticelReader
//{
//    public async Task<string> ReadAsync(string ariticeId)
//    {
//        var response = await new HttpClient().GetAsync($"https://example.com/ariticels/{ariticeId}");
//        return await response.Content.ReadAsStringAsync();
//    }
//}

internal class ArticelReader : IDisposable
{
    private HttpClient? _httpClient;
    public async Task<string> ReadAsync(string ariticeId)
    {
        _httpClient ??= new HttpClient();
        using var response = await _httpClient.GetAsync($"https://example.com/ariticels/{ariticeId}"));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }
    public void Dispose() => _httpClient?.Dispose();
}
