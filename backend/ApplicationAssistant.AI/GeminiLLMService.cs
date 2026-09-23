using System.Text;
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

    private static string CleanJsonResponse(string responseText) =>
        responseText
            .Replace("```json", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("```", string.Empty, StringComparison.Ordinal)
            .Trim();

    private static T DeserializeResponse<T>(string responseText)
    {
        var cleaned = CleanJsonResponse(responseText);
        return JsonSerializer.Deserialize<T>(cleaned, JsonOptions)
            ?? throw new Exception("Gemini returned JSON that deserialized to null.");
    }

    /// <summary>
    /// Closes open strings / arrays / objects when the model truncates mid-JSON.
    /// </summary>
    internal static string TryRepairTruncatedJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return json;
        }

        var inString = false;
        var escape = false;
        var stack = new Stack<char>();

        foreach (var c in json)
        {
            if (inString)
            {
                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (c == '\\')
                {
                    escape = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                case '[':
                    stack.Push(c);
                    break;
                case '}':
                case ']':
                    if (stack.Count > 0)
                    {
                        stack.Pop();
                    }

                    break;
            }
        }

        var sb = new StringBuilder(json);

        if (inString)
        {
            // Drop a trailing unfinished escape so the closing quote is valid.
            if (sb.Length > 0 && sb[^1] == '\\')
            {
                sb.Length--;
            }

            sb.Append('"');
        }

        while (sb.Length > 0)
        {
            var last = sb[^1];
            if (last is ',' or ':' || char.IsWhiteSpace(last))
            {
                sb.Length--;
                continue;
            }

            break;
        }

        while (stack.Count > 0)
        {
            sb.Append(stack.Pop() == '{' ? '}' : ']');
        }

        return sb.ToString();
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
            var responseText = await GenerateJsonAsync(prompt, systemSuffix, inputHistory, temperature, maxTokens);
            if (TryDeserializeResponse<T>(responseText, out var value))
            {
                _logger.LogTrace("GetResponseAsync<{Type}> (sample): returned ({Length} chars)", typeof(T).Name, responseText.Length);
                return value!;
            }

            if (maxTokens < 65536)
            {
                _logger.LogWarning(
                    "JSON deserialize failed ({Length} chars); retrying with more output tokens. Preview: {Preview}",
                    responseText.Length,
                    responseText.Length > 400 ? responseText[^400..] : responseText);

                var retryText = await GenerateJsonAsync(prompt, systemSuffix, inputHistory, temperature, 65536);
                if (TryDeserializeResponse<T>(retryText, out value))
                {
                    _logger.LogTrace("GetResponseAsync<{Type}> (sample retry): returned ({Length} chars)", typeof(T).Name, retryText.Length);
                    return value!;
                }

                responseText = retryText;
            }

            // Last resort: repair truncated JSON (may drop trailing experiences).
            var repaired = TryRepairTruncatedJson(CleanJsonResponse(responseText));
            _logger.LogWarning("Applying truncated-JSON repair ({OriginalLength} → {RepairedLength} chars)", responseText.Length, repaired.Length);
            return JsonSerializer.Deserialize<T>(repaired, JsonOptions)
                ?? throw new Exception("Gemini returned JSON that deserialized to null.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetResponseAsync<{Type}> (sample) failed: {Message}", typeof(T).Name, ex.Message);
            throw;
        }
    }

    private static bool TryDeserializeResponse<T>(string responseText, out T? value)
    {
        try
        {
            value = DeserializeResponse<T>(responseText);
            return true;
        }
        catch (JsonException)
        {
            value = default;
            return false;
        }
    }

    private async Task<string> GenerateJsonAsync(
        string prompt,
        string systemSuffix,
        IEnumerable<InputItem> inputHistory,
        decimal? temperature,
        int maxTokens)
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

        return ReadText(response);
    }
}
