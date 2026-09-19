using System.Text.Json;
using GenerativeAI;
using GenerativeAI.Types;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ApplicationAssistant.AI;

public class GeminiLLMService : ILLMService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IConfiguration _configuration;
    private readonly ILogger<GeminiLLMService> _logger;

    public GeminiLLMService(IConfiguration configuration, ILogger<GeminiLLMService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    private string GetApiKey() =>
        _configuration["Gemini:ApiKey"]
        ?? _configuration["Gemini:APIKey"]
        ?? throw new InvalidOperationException("Gemini API key is not configured (Gemini:ApiKey).");

    private static int EstimateInputTokens(string prompt, IEnumerable<InputItem> inputHistory, string? extraText = null)
    {
        var promptLen = prompt?.Length ?? 0;
        var historyLen = inputHistory?.Sum(i => i.Text?.Length ?? 0) ?? 0;
        var extraLen = extraText?.Length ?? 0;
        var total = promptLen + historyLen + extraLen;
        return (int)Math.Ceiling(total / 4.0);
    }

    private static List<Part> ToParts(InputItem item)
    {
        var parts = new List<Part>();
        if (item.Data is { Length: > 0 } && !string.IsNullOrWhiteSpace(item.MimeType))
        {
            parts.Add(new Part
            {
                InlineData = new Blob
                {
                    MimeType = item.MimeType,
                    Data = Convert.ToBase64String(item.Data)
                }
            });
        }

        if (!string.IsNullOrWhiteSpace(item.Text))
        {
            parts.Add(new Part(item.Text));
        }

        return parts;
    }

    private static List<Content> ToContents(IEnumerable<InputItem> inputHistory) =>
        inputHistory.Select(i => new Content
        {
            Role = i.FromUser ? Roles.User : Roles.Model,
            Parts = ToParts(i)
        }).ToList();

    private static string ReadText(GenerateContentResponse response)
    {
        if (string.IsNullOrWhiteSpace(response.Text))
        {
            throw new Exception("Gemini request returned empty");
        }

        return response.Text.Trim();
    }

    private static T DeserializeResponse<T>(string responseText)
    {
        var cleaned = responseText
            .Replace("```json", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("```", string.Empty, StringComparison.Ordinal)
            .Trim();

        return JsonSerializer.Deserialize<T>(cleaned, JsonOptions)
            ?? throw new Exception("Gemini returned JSON that deserialized to null.");
    }

    public async Task<string> GetResponseAsync(string prompt, IEnumerable<InputItem> inputHistory, decimal? temperature = 0.5m, int maxTokens = 8192)
    {
        var inputTokens = EstimateInputTokens(prompt, inputHistory);
        _logger.LogTrace("GetResponseAsync: estimated input tokens = {InputTokens}", inputTokens);

        var client = new GoogleAi(GetApiKey());
        var googleModel = client.CreateGenerativeModel("models/gemini-2.5-flash");
        var response = await googleModel.GenerateContentAsync(new GenerateContentRequest
        {
            SystemInstruction = new Content { Parts = [new Part(prompt)] },
            Contents = ToContents(inputHistory),
            GenerationConfig = new GenerationConfig
            {
                Temperature = (float?)temperature,
                MaxOutputTokens = maxTokens
            }
        });

        _logger.LogTrace("GetResponseAsync: returned");
        return ReadText(response);
    }

    public async Task<string> GetResponseAsync(string prompt, IEnumerable<InputItem> inputHistory, IEnumerable<string> allowedValues, decimal? temperature = 0.5m, int maxTokens = 8192)
    {
        var allowedValuesText = allowedValues != null ? string.Join(" ", allowedValues) : null;
        var inputTokens = EstimateInputTokens(prompt, inputHistory, allowedValuesText);
        _logger.LogTrace("GetResponseAsync (allowedValues): estimated input tokens = {InputTokens}", inputTokens);

        var client = new GoogleAi(GetApiKey());
        var googleModel = client.CreateGenerativeModel("models/gemini-2.5-flash");

        var allowedValuesArray = (allowedValues ?? []).ToArray();
        var schema = new Schema
        {
            Type = SchemaType.STRING.ToString(),
            Enum = allowedValuesArray.ToList()
        };

        var request = new GenerateContentRequest
        {
            SystemInstruction = new Content { Parts = [new Part(prompt)] },
            Contents = ToContents(inputHistory),
            GenerationConfig = new GenerationConfig
            {
                Temperature = (float?)temperature,
                MaxOutputTokens = maxTokens,
                ResponseSchema = schema
            }
        };

        var response = await googleModel.GenerateContentAsync(request);

        _logger.LogTrace("GetResponseAsync: (allowedValues) returned");
        return ReadText(response);
    }

    public async Task<T> GetResponseAsync<T>(string prompt, IEnumerable<InputItem> inputHistory, decimal? temperature = 0.5m, int maxTokens = 8192)
    {
        var inputTokens = EstimateInputTokens(prompt, inputHistory);
        _logger.LogTrace("GetResponseAsync<{Type}>: estimated input tokens = {InputTokens}", typeof(T).Name, inputTokens);

        var client = new GoogleAi(GetApiKey());
        var googleModel = client.CreateGenerativeModel("models/gemini-2.5-flash");
        var response = await googleModel.GenerateContentAsync(new GenerateContentRequest
        {
            SystemInstruction = new Content { Parts = [new Part(prompt)] },
            Contents = ToContents(inputHistory),
            GenerationConfig = new GenerationConfig
            {
                Temperature = (float?)temperature,
                MaxOutputTokens = maxTokens
            }
        });

        var responseText = ReadText(response);
        try
        {
            _logger.LogTrace("GetResponseAsync<{Type}>: returned", typeof(T).Name);
            return DeserializeResponse<T>(responseText);
        }
        catch (Exception)
        {
            _logger.LogError("Failed to deserialize response: {Response}", responseText);
            throw;
        }
    }

    public async Task<T> GetResponseAsync<T>(T sample, string prompt, IEnumerable<InputItem> inputHistory, decimal? temperature = 0.5m, int maxTokens = 8192)
    {
        var sampleJson = JsonSerializer.Serialize(sample);
        var systemSuffix = $"CRITICAL: Return a JSON like this ```json{sampleJson}```, NEVER return anything else than this json.";
        var inputTokens = EstimateInputTokens(prompt, inputHistory, systemSuffix);
        _logger.LogTrace("GetResponseAsync<{Type}> (sample): estimated input tokens = {InputTokens}", typeof(T).Name, inputTokens);

        try
        {
            var client = new GoogleAi(GetApiKey());
            var googleModel = client.CreateGenerativeModel("models/gemini-2.5-flash");
            var response = await googleModel.GenerateContentAsync(new GenerateContentRequest
            {
                SystemInstruction = new Content { Parts = [new Part($"{prompt}\n\n{systemSuffix}")] },
                Contents = ToContents(inputHistory),
                GenerationConfig = new GenerationConfig
                {
                    Temperature = (float?)temperature,
                    MaxOutputTokens = maxTokens,
                    ResponseMimeType = "application/json"
                }
            });

            var responseText = ReadText(response);
            try
            {
                _logger.LogTrace("GetResponseAsync<{Type}> (sample): returned ({Length} chars)", typeof(T).Name, responseText.Length);
                return DeserializeResponse<T>(responseText);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deserialize response: {Response}", responseText);
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetResponseAsync<{Type}> (sample) failed: {Message}", typeof(T).Name, ex.Message);
            throw;
        }
    }
}
