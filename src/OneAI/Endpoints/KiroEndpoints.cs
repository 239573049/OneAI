using Microsoft.AspNetCore.Mvc;
using OneAI.Services;
using OneAI.Services.AI;
using OneAI.Services.AI.Anthropic;
using Thor.Abstractions.Chats.Dtos;

namespace OneAI.Endpoints;

public static class KiroEndpoints
{
    public static void MapKiroEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/kiro/v1/messages", async (
            KiroService kiroService,
            HttpContext context,
            AnthropicInput input,
            AIAccountService aiAccountService) =>
        {
            await kiroService.ExecuteMessagesAsync(context, input, aiAccountService);
        });

        endpoints.MapPost("/kiro/v1/chat/completions", async (
            KiroService kiroService,
            HttpContext context,
            [FromBody] ThorChatCompletionsRequest request,
            AIAccountService aiAccountService) =>
        {
            await kiroService.ExecuteChatCompletionsAsync(context, request, aiAccountService);
        });
    }
}
