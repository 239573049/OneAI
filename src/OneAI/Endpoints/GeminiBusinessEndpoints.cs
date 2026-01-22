using Microsoft.AspNetCore.Mvc;
using OneAI.Services;
using OneAI.Services.AI;
using OneAI.Services.AI.Gemini;
using Thor.Abstractions.Chats.Dtos;

namespace OneAI.Endpoints;

public static class GeminiBusinessEndpoints
{
    public static void MapGeminiBusinessEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/gemini-business/v1beta/models/{model}:generateContent", HandleGenerateContent)
            .WithName("GeminiBusinessGenerateContent");

        endpoints.MapPost("/gemini-business/v1beta/models/{model}:streamGenerateContent", HandleStreamGenerateContent)
            .WithName("GeminiBusinessStreamGenerateContent");

        endpoints.MapPost("/gemini-business/v1/chat/completions", async (
            GeminiBusinessService geminiBusinessService,
            HttpContext context,
            [FromBody] ThorChatCompletionsRequest request,
            AIAccountService aiAccountService) =>
        {
            await geminiBusinessService.ExecuteChatCompletionsAsync(context, request, request.PromptCacheKey, aiAccountService);
        }).WithName("GeminiBusinessChatCompletions");
    }

    private static async Task HandleGenerateContent(
        string model,
        HttpContext context,
        GeminiBusinessService geminiBusinessService,
        GeminiInput input,
        AIAccountService aiAccountService)
    {
        var conversationId = context.Request.Headers.TryGetValue("conversation_id", out var convId)
            ? convId.ToString()
            : null;

        await geminiBusinessService.ExecuteGenerateContentAsync(context, input, model, conversationId, aiAccountService);
    }

    private static async Task HandleStreamGenerateContent(
        string model,
        HttpContext context,
        GeminiBusinessService geminiBusinessService,
        GeminiInput input,
        AIAccountService aiAccountService)
    {
        var conversationId = context.Request.Headers.TryGetValue("conversation_id", out var convId)
            ? convId.ToString()
            : null;

        await geminiBusinessService.ExecuteStreamGenerateContentAsync(context, input, model, conversationId, aiAccountService);
    }
}

