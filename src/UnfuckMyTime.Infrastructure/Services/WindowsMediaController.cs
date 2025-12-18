using System;
using System.Threading.Tasks;
using UnfuckMyTime.Core.Services;
using Windows.Media.Control;

namespace UnfuckMyTime.Infrastructure.Services
{
    public class WindowsMediaController : IMediaController
    {
        public async Task<bool> TryPausePlaybackAsync()
        {
            try
            {
                var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                var session = manager.GetCurrentSession();

                if (session != null)
                {
                    var playbackInfo = session.GetPlaybackInfo();
                    if (playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    {
                        // Optimization: Check if the source is a browser?
                        // string appId = session.SourceAppUserModelId.ToLowerInvariant();
                        // if (appId.Contains("chrome") || appId.Contains("edge") || appId.Contains("firefox") || appId.Contains("opera"))
                        // {
                        await session.TryPauseAsync();
                        return true;
                        // }
                    }
                }
            }
            catch
            {
                // Setup permissions or non-supported OS might cause throws
            }
            return false;
        }
    }
}
