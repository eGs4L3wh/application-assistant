namespace ApplicationAssistant.AI;

public interface ILLMService
{
    Task<string> GetResponseAsync(string prompt, IEnumerable<InputItem> inputHistory, decimal? temperature = 0.5m, int maxTokens = 8192);
    Task<string> GetResponseAsync(string prompt, IEnumerable<InputItem> inputHistory, IEnumerable<string> allowedValues, decimal? temperature = 0.5m, int maxTokens = 8192);
    Task<T> GetResponseAsync<T>(string prompt, IEnumerable<InputItem> inputHistory, decimal? temperature = 0.5m, int maxTokens = 8192);
    Task<T> GetResponseAsync<T>(T sample, string prompt, IEnumerable<InputItem> inputHistory, decimal? temperature = 0.5m, int maxTokens = 8192);
}

public class InputItem
{
    public required bool FromUser { get; set; }
    public required string Text { get; set; }
    public byte[]? Data { get; set; }
    public string? MimeType { get; set; }
}
