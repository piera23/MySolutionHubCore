using Application.Interfaces;
using Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using System.Text.RegularExpressions;

namespace Infrastructure.Hubs
{
    [Authorize]
    public class ChatHub : Hub<IChatHub>
    {
        private readonly ITenantContext _tenantContext;
        private readonly ILogger<ChatHub> _logger;

        public ChatHub(
            ITenantContext tenantContext,
            ILogger<ChatHub> logger)
        {
            _tenantContext = tenantContext;
            _logger = logger;
        }

        public override async Task OnConnectedAsync()
        {
            var userId = GetUserId();
            var tenantId = _tenantContext.TenantId;

            // Gruppo tenant:user per messaggi diretti
            await Groups.AddToGroupAsync(
                Context.ConnectionId,
                $"{tenantId}:user:{userId}");

            // Notifica gli altri che l'utente è online
            await Clients
                .GroupExcept($"{tenantId}:all", Context.ConnectionId)
                .UserOnline(userId);

            // Aggiungi al gruppo tenant
            await Groups.AddToGroupAsync(
                Context.ConnectionId,
                $"{tenantId}:all");

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = GetUserId();
            var tenantId = _tenantContext.TenantId;

            // Notifica gli altri che l'utente è offline
            await Clients
                .Group($"{tenantId}:all")
                .UserOffline(userId);

            await base.OnDisconnectedAsync(exception);
        }

        // Unisciti a una conversazione specifica
        public async Task JoinConversation(int conversationId)
        {
            var tenantId = _tenantContext.TenantId;
            await Groups.AddToGroupAsync(
                Context.ConnectionId,
                $"{tenantId}:conv:{conversationId}");
        }

        // Lascia una conversazione
        public async Task LeaveConversation(int conversationId)
        {
            var tenantId = _tenantContext.TenantId;
            await Groups.RemoveFromGroupAsync(
                Context.ConnectionId,
                $"{tenantId}:conv:{conversationId}");
        }

        // Indicatore di digitazione — effimero, non persistito
        public async Task SendTyping(int conversationId, bool isTyping)
        {
            var userId = GetUserId();
            var tenantId = _tenantContext.TenantId;
            var username = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "";

            await Clients
                .GroupExcept($"{tenantId}:conv:{conversationId}", Context.ConnectionId)
                .UserTyping(new TypingDto(conversationId, userId, username, isTyping));
        }

        private int GetUserId()
        {
            var claim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(claim, out var id) ? id : 0;
        }
    }
}
