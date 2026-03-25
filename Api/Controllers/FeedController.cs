using Api.Filters;
using Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.ComponentModel.DataAnnotations;

namespace Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/v{version:apiVersion}/[controller]")]
    [RequireFeature("social:feed")]
    [EnableRateLimiting("api")]
    public class FeedController : TenantApiControllerBase
    {
        private readonly IActivityService _activityService;

        public FeedController(IActivityService activityService)
        {
            _activityService = activityService;
        }

        [HttpGet]
        public async Task<IActionResult> GetFeed(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? cursor = null)
        {
            var userId = GetUserId();

            if (!string.IsNullOrEmpty(cursor))
            {
                var result = await _activityService.GetFeedPageAsync(userId, cursor, pageSize);
                return Ok(result);
            }

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

        public record PublishRequest(
            [Required, MaxLength(100)] string EventType,
            [MaxLength(5000)] string? Payload);
    }
}
