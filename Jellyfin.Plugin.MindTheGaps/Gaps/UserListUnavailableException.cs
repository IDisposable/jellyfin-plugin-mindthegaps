using System;
using System.IO;

namespace Jellyfin.Plugin.MindTheGaps.Gaps;

/// <summary>
/// A user's list could not be read or written just now (an I/O error, a permission, a lock), so a change to it
/// is refused: never written over what is on disk, and never reported as made when it was not saved.
/// </summary>
internal sealed class UserListUnavailableException : IOException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UserListUnavailableException"/> class.
    /// </summary>
    public UserListUnavailableException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UserListUnavailableException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    public UserListUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UserListUnavailableException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The read or write failure.</param>
    public UserListUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
