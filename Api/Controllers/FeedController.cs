using Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class FeedController : ControllerBase
    {
        private readonly IActivityService _activityService;

        public FeedController(IActivityService activityService)
        {
            _activityService = activityService;
        }

        [HttpGet]
        public async Task<IActionResult> GetFeed([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            var userId = GetUserId();
            var feed = await _activityService.GetFeedAsync(userId, page, pageSize);
            return Ok(feed);
        }

        [HttpPost("{id}/react")]
        public async Task<IActionResult> React(int id, [FromQuery] string type = "like")
        {
            var userId = GetUserId();
            await _activityService.ReactAsync(id, userId, type);
            return NoContent();
        }

        [HttpPost("follow/{followedId}")]
        public async Task<IActionResult> Follow(int followedId)
        {
            var userId = GetUserId();
            await _activityService.FollowAsync(userId, followedId);
            return NoContent();
        }

        [HttpDelete("follow/{followedId}")]
        public async Task<IActionResult> Unfollow(int followedId)
        {
            var userId = GetUserId();
            await _activityService.UnfollowAsync(userId, followedId);
            return NoContent();
        }

        private int GetUserId()
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(claim, out var id) ? id : 0;
        }

        [HttpPost("publish")]
        public async Task<IActionResult> Publish([FromBody] PublishRequest request)
        {
            var userId = GetUserId();
            await _activityService.PublishAsync(
                userId,
                request.EventType,
                payload: new { text = request.Payload });
            return NoContent();
        }

        public record PublishRequest(string EventType, string? Payload);
    }
}
