using Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class ChatController : ControllerBase
    {
        private readonly IChatService _chatService;

        public ChatController(IChatService chatService)
        {
            _chatService = chatService;
        }

        [HttpGet("conversations")]
        public async Task<IActionResult> GetConversations()
        {
            var userId = GetUserId();
            var conversations = await _chatService.GetUserConversationsAsync(userId);
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

        private int GetUserId()
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(claim, out var id) ? id : 0;
        }
    }

    public record CreateGroupRequest(string Title, IEnumerable<int> MemberIds);
    public record SendMessageRequest(string Body, string? AttachmentUrl);
}
