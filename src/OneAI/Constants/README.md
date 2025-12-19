# AI 提供商常量使用指南

## AIProviders 常量类

[AIProviders.cs](AIProviders.cs) 提供了所有支持的 AI 提供商常量定义。

### 支持的提供商

#### 国际主流 AI 服务
- **OpenAI** - ChatGPT, GPT-4, GPT-3.5 等
- **Claude** - Anthropic Claude 3 系列 (Opus, Sonnet, Haiku)
- **Gemini** - Google Gemini Pro, Ultra 等
- **PaLM** - Google PaLM 2
- **Cohere** - Cohere AI
- **HuggingFace** - Hugging Face 模型
- **AzureOpenAI** - Microsoft Azure OpenAI 服务
- **Bedrock** - Amazon Bedrock

#### 国内 AI 服务
- **Qwen** - 阿里云通义千问
- **Ernie** - 百度文心一言
- **Spark** - 讯飞星火
- **ChatGLM** - 智谱 ChatGLM
- **Moonshot** - 月之暗面 (Kimi)
- **MiniMax** - MiniMax

#### 其他
- **Custom** - 自定义提供商

## 使用示例

### 1. 使用常量创建账户

```csharp
using OneAI.Constants;
using OneAI.Entities;

var account = new AIAccount
{
    Provider = AIProviders.OpenAI,  // 使用常量
    ApiKey = "sk-xxx",
    Name = "我的 OpenAI 账户",
    BaseUrl = "https://api.openai.com/v1"
};
```

### 2. 验证提供商是否有效

```csharp
using OneAI.Constants;

string provider = "OpenAI";
if (AIProviders.IsValid(provider))
{
    Console.WriteLine($"{provider} 是有效的提供商");
}
```

### 3. 获取提供商显示名称

```csharp
using OneAI.Constants;

string displayName = AIProviders.GetDisplayName(AIProviders.Claude);
Console.WriteLine(displayName); // 输出: Anthropic Claude
```

### 4. 获取所有提供商列表

```csharp
using OneAI.Constants;

foreach (var provider in AIProviders.All)
{
    var displayName = AIProviders.GetDisplayName(provider);
    Console.WriteLine($"{provider}: {displayName}");
}
```

### 5. 在 API 中使用

```csharp
// 创建账户时验证提供商
public async Task<IResult> CreateAccount([FromBody] CreateAccountRequest request)
{
    if (!AIProviders.IsValid(request.Provider))
    {
        return Results.BadRequest($"不支持的提供商: {request.Provider}");
    }

    var account = new AIAccount
    {
        Provider = request.Provider,
        ApiKey = request.ApiKey,
        Name = request.Name
    };

    // 保存到数据库...
    return Results.Ok(account);
}
```

### 6. 在前端下拉框中使用

```csharp
// API 端点返回所有提供商
app.MapGet("/api/providers", () =>
{
    var providers = AIProviders.All.Select(p => new
    {
        Value = p,
        Label = AIProviders.GetDisplayName(p)
    });

    return Results.Json(ApiResponse<object>.Success(providers));
});
```

## 添加新提供商

如需添加新的 AI 提供商，请在 [AIProviders.cs](AIProviders.cs) 中：

1. 添加常量定义
2. 添加到 `All` 数组
3. 在 `GetDisplayName` 方法中添加显示名称映射

```csharp
public const string NewProvider = "NewProvider";

public static readonly string[] All =
[
    // ... 现有提供商
    NewProvider
];

public static string GetDisplayName(string provider)
{
    return provider switch
    {
        // ... 现有映射
        NewProvider => "新提供商显示名称",
        _ => provider
    };
}
```
