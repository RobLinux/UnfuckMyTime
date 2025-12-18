using System.Threading.Tasks;

namespace UnfuckMyTime.Core.Services
{
    public interface IMediaController
    {
        /// <summary>
        /// Attempts to pause any active media playback if it comes from a distracting source.
        /// </summary>
        /// <returns>True if pause signal was sent.</returns>
        Task<bool> TryPausePlaybackAsync();
    }
}
