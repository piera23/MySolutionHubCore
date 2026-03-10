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

            // Hub notifiche
            _notificationHub = new HubConnectionBuilder()
                .WithUrl($"{baseUrl}/hubs/notifications", options =>
                {
                    options.AccessTokenProvider = () =>
                        Task.FromResult(_authState.Token);
                    options.Headers.Add("X-Tenant-Id", "cliente1");
                })
                .WithAutomaticReconnect()
                .Build();

            _notificationHub.On<NotificationDto>("ReceiveNotification", notification =>
            {
                OnNotificationReceived?.Invoke(notification);
            });

            // Hub chat
            _chatHub = new HubConnectionBuilder()
                .WithUrl($"{baseUrl}/hubs/chat", options =>
                {
                    options.AccessTokenProvider = () =>
                        Task.FromResult(_authState.Token);
                    options.Headers.Add("X-Tenant-Id", "cliente1");
                })
                .WithAutomaticReconnect()
                .Build();

            _chatHub.On<ChatMessageDto>("ReceiveMessage", message =>
            {
                OnMessageReceived?.Invoke(message);
            });

            _chatHub.On<TypingDto>("UserTyping", typing =>
            {
                OnUserTyping?.Invoke(typing);
            });

            try
            {
                await _notificationHub.StartAsync();
                await _chatHub.StartAsync();
                Console.WriteLine("SignalR connesso!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Errore SignalR: {ex.Message}");
            }
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
