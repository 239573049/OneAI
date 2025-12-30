using System.Text.Json;
using System.Text.Json.Serialization;
using OneAI.Constants;
using OneAI.Services;

namespace OneAI.Services.AI;

public interface IModelMappingService
{
    Task<ModelMappingResult?> ResolveAnthropicAsync(string model);
    Task<ModelMappingResult?> ResolveOpenAiChatAsync(string model);
}

public sealed record ModelMappingResult(string TargetModel, string? TargetProvider);

public sealed class ModelMappingService(ISettingsService settingsService, ILogger<ModelMappingService> logger)
    : IModelMappingService
{
    private readonly ISettingsService _settingsService = settingsService;
    private readonly ILogger<ModelMappingService> _logger = logger;
    private string? _cachedRaw;
    private ModelMappingConfig _cachedConfig = new();

    public async Task<ModelMappingResult?> ResolveAnthropicAsync(string model)
    {
        var config = await GetConfigAsync();
        return ResolveRule(config.Anthropic, model, _logger);
    }

    public async Task<ModelMappingResult?> ResolveOpenAiChatAsync(string model)
    {
        var config = await GetConfigAsync();
        return ResolveRule(config.OpenAiChat, model, _logger);
    }

    private async Task<ModelMappingConfig> GetConfigAsync()
    {
        var raw = await _settingsService.GetSettingAsync(SettingsKeys.Model_Mapping_Rules);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new ModelMappingConfig();
        }

        if (string.Equals(raw, _cachedRaw, StringComparison.Ordinal))
        {
            return _cachedConfig;
        }

        try
        {
            var config = JsonSerializer.Deserialize<ModelMappingConfig>(raw, JsonSerializerOptions.Web)
                         ?? new ModelMappingConfig();
            _cachedRaw = raw;
            _cachedConfig = config;
            return config;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "模型映射配置解析失败，将回退默认映射");
            _cachedRaw = raw;
            _cachedConfig = new ModelMappingConfig();
            return _cachedConfig;
        }
    }

    private static ModelMappingResult? ResolveRule(
        IReadOnlyList<ModelMappingRule>? rules,
        string model,
        ILogger logger)
    {
        if (rules == null || rules.Count == 0)
        {
            return null;
        }

        var normalizedModel = model?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedModel))
        {
            return null;
        }

        var rule = rules.FirstOrDefault(r =>
            string.Equals(r.Source?.Trim(), normalizedModel, StringComparison.OrdinalIgnoreCase));

        if (rule == null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(rule.TargetModel))
        {
            logger.LogWarning("模型映射规则缺少 target_model，已忽略 source={Source}", rule.Source);
            return null;
        }

        var provider = NormalizeProvider(rule.TargetProvider);
        if (!string.IsNullOrWhiteSpace(rule.TargetProvider) && provider == null)
        {
            logger.LogWarning("模型映射规则 target_provider 无效，已忽略 provider={Provider}", rule.TargetProvider);
        }

        return new ModelMappingResult(rule.TargetModel.Trim(), provider);
    }

    private static string? NormalizeProvider(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return null;
        }

        var normalized = provider.Trim()
            .Replace("_", "-", StringComparison.Ordinal)
            .Replace(" ", "-", StringComparison.Ordinal)
            .ToLowerInvariant();

        return normalized switch
        {
            "gemini" => AIProviders.Gemini,
            "gemini-antigravity" => AIProviders.GeminiAntigravity,
            "geminiantigravity" => AIProviders.GeminiAntigravity,
            "antigravity" => AIProviders.GeminiAntigravity,
            _ => null
        };
    }
}

public sealed class ModelMappingConfig
{
    [JsonPropertyName("anthropic")] public List<ModelMappingRule> Anthropic { get; set; } = new();

    [JsonPropertyName("openai_chat")] public List<ModelMappingRule> OpenAiChat { get; set; } = new();
}

public sealed class ModelMappingRule
{
    [JsonPropertyName("source")] public string? Source { get; set; }

    [JsonPropertyName("target_provider")] public string? TargetProvider { get; set; }

    [JsonPropertyName("target_model")] public string? TargetModel { get; set; }
}
