using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicIsland.Core.Diagnostics;
using DynamicIsland.Core.Notifications;

namespace DynamicIsland.UI.ViewModels;

/// <summary>A notification kept in the island's history list.</summary>
public sealed partial class HistoryItem(IslandNotification notification, Bitmap? icon) : ObservableObject
{
    public IslandNotification Notification { get; } = notification;
    public Bitmap? Icon { get; } = icon;
    public bool HasIcon => Icon is not null;
    public string AppName => Notification.AppName;

    [ObservableProperty] public partial string Title { get; set; } = "";
    [ObservableProperty] public partial string TimeText { get; set; } = "";
}

public sealed partial class IslandViewModel
{
    private const int MaxHistory = 30;
    private const int VisibleHistoryRows = 4;
    private const double HistoryRowHeight = 56;

    private readonly INotificationService _notifications;
    private readonly List<IslandNotification> _notificationQueue = [];

    [ObservableProperty] public partial bool HasNotification { get; private set; }
    [ObservableProperty] public partial string NotificationApp { get; private set; } = "";
    [ObservableProperty] public partial string NotificationTitle { get; private set; } = "";
    [ObservableProperty] public partial string NotificationBody { get; private set; } = "";
    [ObservableProperty] public partial Bitmap? NotificationIcon { get; private set; }
    [ObservableProperty] public partial string NotificationMoreText { get; private set; } = "";

    public bool HasNotificationIcon => NotificationIcon is not null;
    public bool HasNotificationBody => NotificationBody.Length > 0;
    public bool HasMoreNotifications => NotificationMoreText.Length > 0;
    public string MuteAppToolTip => $"Don't show notifications from {NotificationApp}";

    private IslandNotification? CurrentNotification => _notificationQueue.LastOrDefault();

    // ---- History -----------------------------------------------------------------------------

    /// <summary>Newest first. Kept in memory only; cleared when the app exits.</summary>
    public ObservableCollection<HistoryItem> History { get; } = [];

    public bool HasHistory => History.Count > 0;
    public string HistoryCountText => History.Count > 9 ? "9+" : History.Count.ToString();

    private double HistoryHeight =>
        History.Count == 0 ? 132 : 64 + Math.Min(History.Count, VisibleHistoryRows) * HistoryRowHeight + 14;

    [RelayCommand]
    private void OpenHistory()
    {
        RefreshHistoryTimes();
        NavigateTo(ExpandedPage.History);
    }

    [RelayCommand]
    private void ClearHistory()
    {
        History.Clear();
        OnHistoryChanged();
    }

    [RelayCommand]
    private void OpenHistoryItem(HistoryItem item)
    {
        _notifications.OpenApp(item.Notification);
        RemoveHistoryItem(item);
        Collapse();
    }

    [RelayCommand]
    private void RemoveHistoryItem(HistoryItem item)
    {
        History.Remove(item);
        OnHistoryChanged();
        _ = _notifications.DismissAsync(item.Notification);
    }

    private void AddToHistory(IslandNotification notification)
    {
        var (icon, _) = ArtworkLoader.Load(notification.AppIcon);
        History.Insert(0, new HistoryItem(notification, icon));
        while (History.Count > MaxHistory)
            History.RemoveAt(History.Count - 1);

        RefreshHistoryTimes();
        OnHistoryChanged();
    }

    private void RefreshHistoryTimes()
    {
        var now = DateTimeOffset.Now;
        foreach (var item in History)
        {
            item.TimeText = RelativeTime(now - item.Notification.Arrived);
            item.Title = Settings.HideNotificationPreviews ? "New notification" : item.Notification.Title;
        }
    }

    private static string RelativeTime(TimeSpan age) => age.TotalMinutes switch
    {
        < 1 => "now",
        < 60 => $"{(int)age.TotalMinutes}m",
        < 60 * 24 => $"{(int)age.TotalHours}h",
        _ => $"{(int)age.TotalDays}d",
    };

    private void OnHistoryChanged()
    {
        OnPropertyChanged(nameof(HasHistory));
        OnPropertyChanged(nameof(HistoryCountText));
        if (ShowExpandedHistory)
            UpdateShape();
    }

    // ---- Live notifications --------------------------------------------------------------------

    private void OnNotificationReceived(object? sender, IslandNotification notification) =>
        Dispatcher.UIThread.Post(() => ShowNotification(notification));

    /// <summary>Shows a notification; also used by the <c>--demo</c> mode.</summary>
    public void ShowNotification(IslandNotification notification)
    {
        if (!Settings.ShowNotifications || Settings.IsMuted(notification.AppName)
            || (notification.FromAppWindow && !Settings.ReadTeamsPopups))
        {
            DebugLog.Write($"Island: ignored notification from {notification.AppName} (notifications off or app muted)");
            return;
        }

        DebugLog.Write($"Island: showing notification from {notification.AppName}");
        AddToHistory(notification);

        _notificationQueue.Add(notification);
        if (_notificationQueue.Count > 20)
            _notificationQueue.RemoveAt(0);

        PresentCurrentNotification();
        NotificationArrived?.Invoke(this, EventArgs.Empty);

        if (Settings.ExpandOnNotification || IsExpanded)
        {
            Expand(peek: true);
            if (!_pointerInside)
                RestartCollapseTimer(extraSeconds: 2);
        }
        else
        {
            HasUnseenActivity = true;
        }
    }

    private void PresentCurrentNotification()
    {
        var current = CurrentNotification;
        HasNotification = current is not null;
        if (current is null)
            return;

        var hidePreview = Settings.HideNotificationPreviews;
        NotificationApp = current.AppName;
        NotificationTitle = hidePreview ? "New notification" : current.Title;
        NotificationBody = hidePreview ? "" : current.Body;

        // The previous icon is left to the GC; see ReplaceArtwork for why it is not disposed.
        NotificationIcon = ArtworkLoader.Load(current.AppIcon).Image;

        var more = _notificationQueue.Count - 1;
        NotificationMoreText = more > 0 ? $"+{more} more" : "";
        OnPropertyChanged(nameof(HasNotificationBody));
        OnPropertyChanged(nameof(HasMoreNotifications));
        OnPropertyChanged(nameof(MuteAppToolTip));
    }

    [RelayCommand]
    private void OpenNotification()
    {
        if (CurrentNotification is { } notification)
        {
            _notifications.OpenApp(notification);
            if (History.FirstOrDefault(h => h.Notification == notification) is { } item)
            {
                History.Remove(item);
                OnHistoryChanged();
            }
        }

        Collapse();
    }

    /// <summary>Removes the notification from Windows too, then shows the previous one or closes.</summary>
    [RelayCommand]
    private async Task DismissNotification()
    {
        if (CurrentNotification is not { } notification)
            return;

        _notificationQueue.Remove(notification);
        if (History.FirstOrDefault(h => h.Notification == notification) is { } item)
        {
            History.Remove(item);
            OnHistoryChanged();
        }

        if (_notificationQueue.Count == 0)
            Collapse();
        else
            PresentCurrentNotification();

        await _notifications.DismissAsync(notification);
    }

    [RelayCommand]
    private void MuteNotificationApp()
    {
        if (CurrentNotification is not { } notification)
            return;

        if (!Settings.IsMuted(notification.AppName))
            _settings.Update(Settings with { MutedNotificationApps = [.. Settings.MutedNotificationApps, notification.AppName] });

        _notificationQueue.RemoveAll(n => n.AppName.Equals(notification.AppName, StringComparison.OrdinalIgnoreCase));
        if (_notificationQueue.Count == 0)
            Collapse();
        else
            PresentCurrentNotification();
    }

    partial void OnHasNotificationChanged(bool value) => UpdateShape();
    partial void OnNotificationIconChanged(Bitmap? value) => OnPropertyChanged(nameof(HasNotificationIcon));
}
