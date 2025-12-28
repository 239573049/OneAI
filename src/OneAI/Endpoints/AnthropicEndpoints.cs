using OneAI.Services;
using OneAI.Services.AI;
using OneAI.Services.AI.Anthropic;

namespace OneAI.Endpoints;

public static class AnthropicEndpoints
{
    public static void MapAnthropicEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/v1/messages", async (
            AnthropicService anthropicService,
            HttpContext context,
            AnthropicInput input,
            AIAccountService aiAccountService) =>
        {
            await anthropicService.Execute(context, input, aiAccountService);
        });

        endpoints.MapPost("/v1/message", async (
            AnthropicService anthropicService,
            HttpContext context,
            AnthropicInput input,
            AIAccountService aiAccountService) =>
        {
            await anthropicService.Execute(context, input, aiAccountService);
        });

        endpoints.MapPost("/v1/messages/count_tokens", async (
            AnthropicService anthropicService,
            HttpContext context,
            AnthropicInput input) =>
        {
            await anthropicService.CountTokens(context, input);
        });
    }
}
