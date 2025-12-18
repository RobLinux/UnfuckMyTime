using System;
using System.Threading.Tasks;
using UnfuckMyTime.Core.Services;
using Windows.Media.Control;

namespace UnfuckMyTime.Infrastructure.Services
{
    public class WindowsMediaController : IMediaController
    {
        public async Task<bool> TryPausePlaybackAsync(Func<string, string, bool> isDistractionCallback)
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
                        var props = await session.TryGetMediaPropertiesAsync();
                        string title = props?.Title ?? string.Empty;
                        string appId = session.SourceAppUserModelId;

                        // Delegate potential distraction decision to the caller (Core Logic)
                        bool isDistraction = isDistractionCallback(appId, title);
                        
                        // If it IS a distraction, pause it.
                        // (If it's allowed or exempt, do nothing).
                        if (isDistraction)
                        {
                            await session.TryPauseAsync();
                            return true;
                        }
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
