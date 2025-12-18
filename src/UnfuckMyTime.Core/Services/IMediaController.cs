using System.Threading.Tasks;

namespace UnfuckMyTime.Core.Services
{
    public interface IMediaController
    {
        /// <summary>
        /// Checks active media sessions and pauses them if the provided predicate determines they are distracting.
        /// </summary>
        /// <param name="isDistraction">A function that takes (processName, songTitle) and returns true if it should be paused.</param>
        Task<bool> TryPausePlaybackAsync(Func<string, string, bool> isDistraction);
    }
}
