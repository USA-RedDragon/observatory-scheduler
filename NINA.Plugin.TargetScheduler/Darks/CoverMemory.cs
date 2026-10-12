using System;

namespace NINA.Plugin.TargetScheduler.Darks {

    /// <summary>
    /// The flat device's cover and light as last reported while it was connected. The sequence powers the device off
    /// after closing the flap, so darks trust the last connected report: Closed with the light off stays Closed until
    /// the device reports something else while connected again.
    /// </summary>
    public class CoverMemory {
        private readonly object sync = new object();
        private DarkCoverState? state;
        private bool lightOn;
        private DateTime? seenAt;

        public void Observe(bool connected, DarkCoverState coverState, bool light, DateTime atUtc) {
            if (!connected) { return; }
            lock (sync) {
                state = coverState;
                lightOn = light;
                seenAt = atUtc;
            }
        }

        public DarkCover Apply(DarkCover live) {
            if (live == null || !live.Configured || live.ReadError != null) { return live; }
            if (live.Connected) {
                Observe(true, live.State, live.LightOn, DateTime.UtcNow);
                return live;
            }
            lock (sync) {
                live.Remembered = state.HasValue;
                if (state.HasValue) {
                    live.State = state.Value;
                    live.LightOn = lightOn;
                    live.SeenAt = seenAt;
                }
            }
            return live;
        }
    }
}
