namespace DynamicIsland.Core.Notifications;

/// <param name="AppId">OS identifier of the sending app (Windows: AppUserModelID), used to open it.</param>
/// <param name="AppIcon">Encoded image bytes, or null when the app provides none.</param>
public sealed record IslandNotification(
    uint Id,
    string AppId,
    string AppName,
    string Title,
    string Body,
    byte[]? AppIcon,
    DateTimeOffset Arrived);

public enum NotificationAccess
{
    /// <summary>This OS has no notification integration yet.</summary>
    Unsupported,

    /// <summary>The app must be registered with the OS first (Windows: package identity).</summary>
    NeedsSetup,

    Unknown,
    Allowed,
    Denied,
}

/// <summary>Platform notification integration (Windows: UserNotificationListener, macOS: Accessibility).</summary>
public interface INotificationService : IDisposable
{
    NotificationAccess Access { get; }

    /// <summary>Raised when a new notification arrives. May be raised on any thread.</summary>
    event EventHandler<IslandNotification>? Received;

    /// <summary>Asks the OS for permission if needed and starts listening. Call on the UI thread.</summary>
    Task<NotificationAccess> StartAsync();

    /// <summary>Removes the notification from the OS notification centre.</summary>
    Task DismissAsync(IslandNotification notification);

    /// <summary>Brings the sending app to the front.</summary>
    void OpenApp(IslandNotification notification);
}

/// <summary>Used until a platform implementation is available.</summary>
public sealed class NullNotificationService : INotificationService
{
    public NotificationAccess Access => NotificationAccess.Unsupported;
    public event EventHandler<IslandNotification>? Received { add { } remove { } }
    public Task<NotificationAccess> StartAsync() => Task.FromResult(NotificationAccess.Unsupported);
    public Task DismissAsync(IslandNotification notification) => Task.CompletedTask;
    public void OpenApp(IslandNotification notification) { }
    public void Dispose() { }
}
