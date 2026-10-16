namespace TimeTug.Presentation.Tug;

/// <summary>
/// The responses a person can give outside the tug window (the toast buttons). Each names the request it was
/// meant for, so a late click on an old toast cannot answer a newer request.
/// </summary>
public interface ITugActions
{
    ValueTask JoinAsync(string eventId);

    ValueTask SnoozeAsync(string eventId, TimeSpan duration);

    ValueTask DismissAsync(string eventId);
}
