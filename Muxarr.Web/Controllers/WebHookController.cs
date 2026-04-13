using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Muxarr.Core.Api.Models;
using Muxarr.Core.Config;
using Muxarr.Data;
using Muxarr.Data.Extensions;
using Muxarr.Web.Services;

namespace Muxarr.Web.Controllers;

public class WebHookController(
    WebhookService webhookService,
    IDbContextFactory<AppDbContext> contextFactory,
    ILogger<WebHookController> logger) : Controller
{
    [HttpPost]
    [Route("~/api/webhook")]
    public async Task<IActionResult> Post([FromBody] WebhookPayload payload, [FromQuery] string? apikey)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var config = await context.Configs.GetAsync<WebhookConfig>();

        if (!string.IsNullOrEmpty(config?.ApiKey) && config.ApiKey != apikey)
        {
            logger.LogWarning("Webhook rejected: invalid or missing API key");
            return Unauthorized();
        }

        if (payload.IsTest)
        {
            logger.LogInformation("Webhook test received");
            return Ok("OK");
        }

        if (!payload.IsActionable)
        {
            logger.LogDebug("Webhook ignoring event type: {EventType}", payload.EventType);
            return Ok("Ignored");
        }

        var items = payload.GetFileItems();
        if (items.Count == 0)
        {
            logger.LogWarning("Webhook received {EventType} event but no file paths found", payload.EventType);
            return Ok("No paths");
        }

        foreach (var item in items)
        {
            webhookService.Enqueue(item);
        }

        logger.LogInformation("Webhook accepted {Count} file(s) from {EventType} event", items.Count,
            payload.EventType);
        return Ok("Accepted");
    }

    [HttpGet]
    [Route("~/api/webhook")]
    public IActionResult Get()
    {
        return Ok("Muxarr webhook endpoint is active.");
    }
}
