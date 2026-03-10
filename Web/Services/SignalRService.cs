using Microsoft.AspNetCore.SignalR.Client;

namespace Web.Services
{
    public class SignalRService : IAsyncDisposable
    {
        private readonly AuthStateService _authState;
        private HubConnection? _notificationHub;
        private HubConnection? _chatHub;

        public event Action<NotificationDto>? OnNotificationReceived;
        public event Action<ChatMessageDto>? OnMessageReceived;
        public event Action<TypingDto>? OnUserTyping;

        public bool IsConnected =>
            _notificationHub?.State == HubConnectionState.Connected;

        public SignalRService(AuthStateService authState)
        {
            _authState = authState;
        }

        public async Task StartAsync(string baseUrl = "http://localhost:5000")
        {
            if (!_authState.IsAuthenticated) return;
            if (_notificationHub?.State == HubConnectionState.Connected) return;

            var tenant = _authState.TenantSubdomain ?? "cliente1";

            _notificationHub = new HubConnectionBuilder()
                .WithUrl($"{baseUrl}/hubs/notifications?tenantId={tenant}", options =>
                {
                    options.AccessTokenProvider = () =>
                        Task.FromResult(_authState.Token);
                })
                .WithAutomaticReconnect()
                .Build();

            _chatHub = new HubConnectionBuilder()
                .WithUrl($"{baseUrl}/hubs/chat?tenantId={tenant}", options =>
                {
                    options.AccessTokenProvider = () =>
                        Task.FromResult(_authState.Token);
                })
                .WithAutomaticReconnect()
                .Build();

            // ... resto invariato
        }

        public async Task JoinConversationAsync(int conversationId)
        {
            if (_chatHub?.State == HubConnectionState.Connected)
                await _chatHub.InvokeAsync("JoinConversation", conversationId);
        }

        public async Task LeaveConversationAsync(int conversationId)
        {
            if (_chatHub?.State == HubConnectionState.Connected)
                await _chatHub.InvokeAsync("LeaveConversation", conversationId);
        }

        public async Task SendTypingAsync(int conversationId)
        {
            if (_chatHub?.State == HubConnectionState.Connected)
                await _chatHub.InvokeAsync("SendTyping", conversationId);
        }

        public async ValueTask DisposeAsync()
        {
            if (_notificationHub is not null)
                await _notificationHub.DisposeAsync();
            if (_chatHub is not null)
                await _chatHub.DisposeAsync();
        }

        // DTOs
        public record NotificationDto(
            int Id, string Type, string Title, string Message,
            string? EntityId, string? EntityType,
            bool IsRead, DateTime CreatedAt);

        public record ChatMessageDto(
            int Id, int ConversationId, int SenderId,
            string SenderUsername, string? SenderAvatar,
            string Body, DateTime SentAt, bool IsOwn);

        public record TypingDto(
            int ConversationId, int UserId, string Username);
    }
}
