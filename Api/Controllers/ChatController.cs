using Api.Filters;
using Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.ComponentModel.DataAnnotations;

namespace Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/v{version:apiVersion}/[controller]")]
    [RequireFeature("social:chat")]
    [EnableRateLimiting("api")]
    public class ChatController : TenantApiControllerBase
    {
        private readonly IChatService _chatService;

        public ChatController(IChatService chatService)
        {
            _chatService = chatService;
        }

        [HttpGet("conversations")]
        public async Task<IActionResult> GetConversations(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            var userId = GetUserId();
            var conversations = await _chatService.GetUserConversationsAsync(userId, page, pageSize);
            return Ok(conversations);
        }

        [HttpPost("conversations/direct/{targetUserId}")]
        public async Task<IActionResult> GetOrCreateDirect(int targetUserId)
        {
            var userId = GetUserId();
            var conversation = await _chatService.GetOrCreateDirectAsync(userId, targetUserId);
            return Ok(conversation);
        }

        [HttpPost("conversations/group")]
        public async Task<IActionResult> CreateGroup([FromBody] CreateGroupRequest request)
        {
            var userId = GetUserId();
            var conversation = await _chatService.CreateGroupAsync(userId, request.Title, request.MemberIds);
            return Ok(conversation);
        }

        [HttpGet("conversations/{conversationId}/messages")]
        public async Task<IActionResult> GetMessages(
            int conversationId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 30)
        {
            var userId = GetUserId();
            var messages = await _chatService.GetMessagesAsync(conversationId, userId, page, pageSize);
            return Ok(messages);
        }

        [HttpPost("conversations/{conversationId}/messages")]
        public async Task<IActionResult> SendMessage(
            int conversationId,
            [FromBody] SendMessageRequest request)
        {
            var userId = GetUserId();
            var message = await _chatService.SendMessageAsync(conversationId, userId, request.Body, request.AttachmentUrl);
            return Ok(message);
        }

        [HttpPut("conversations/{conversationId}/read")]
        public async Task<IActionResult> MarkRead(int conversationId)
        {
            var userId = GetUserId();
            await _chatService.MarkConversationReadAsync(conversationId, userId);
            return NoContent();
        }

    }

    public record CreateGroupRequest(
        [Required, MaxLength(200)] string Title,
        [Required] IEnumerable<int> MemberIds);

    public record SendMessageRequest(
        [Required, MaxLength(4000)] string Body,
        [Url, MaxLength(500)] string? AttachmentUrl);
}
