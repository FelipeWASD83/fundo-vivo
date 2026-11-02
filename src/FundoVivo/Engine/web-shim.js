// Runs before the wallpaper's own scripts. Recreates the functions Wallpaper Engine gives to web wallpapers,
// fed by Fundo Vivo through postMessage.
(() => {
  const host = window.chrome && window.chrome.webview;
  if (!host) return;
  const audioListeners = [];
  const noop = () => {};

  window.wallpaperRegisterAudioListener = (callback) => {
    if (typeof callback !== 'function') return;
    audioListeners.push(callback);
    host.postMessage({ type: 'audio-wanted' });
  };
  // Media integration (song title, cover art) is not provided; registering is accepted and stays silent.
  window.wallpaperRegisterMediaStatusListener = noop;
  window.wallpaperRegisterMediaPropertiesListener = noop;
  window.wallpaperRegisterMediaThumbnailListener = noop;
  window.wallpaperRegisterMediaPlaybackListener = noop;
  window.wallpaperRegisterMediaTimelineListener = noop;
  window.wallpaperRequestRandomFileForProperty = noop;

  const listener = () => window.wallpaperPropertyListener || {};
  // Counters read by the engine's self-test; harmless otherwise.
  const stats = { audioFrames: 0, audioListeners: 0, propsApplied: 0, generalApplied: 0 };
  Object.defineProperty(window, '__fundoVivo', { value: stats });
  let lastProps = null, lastGeneral = null, paused = false, propsDelivered = false;

  host.addEventListener('message', (event) => {
    const m = event.data;
    if (!m || typeof m !== 'object') return;
    try {
      if (m.type === 'audio') {
        stats.audioFrames++;
        stats.audioListeners = audioListeners.length;
        for (const cb of audioListeners) cb(m.data);
      } else if (m.type === 'props') {
        lastProps = Object.assign(lastProps || {}, m.data);
        if (typeof listener().applyUserProperties === 'function') {
          listener().applyUserProperties(propsDelivered ? m.data : lastProps);
          propsDelivered = true;
          stats.propsApplied++;
        }
      } else if (m.type === 'general') {
        lastGeneral = Object.assign(lastGeneral || {}, m.data);
        if (typeof listener().applyGeneralProperties === 'function') { listener().applyGeneralProperties(m.data); stats.generalApplied++; }
      } else if (m.type === 'paused') {
        paused = !!m.data;
        if (typeof listener().setPaused === 'function') listener().setPaused(paused);
      }
    } catch (err) {
      console.error('Fundo Vivo:', err);
    }
  });

  // Some wallpapers create wallpaperPropertyListener only after their own load handler; ask again a bit later
  // so they still receive their settings.
  const ready = () => {
    host.postMessage({ type: 'ready' });
    setTimeout(() => {
      const l = listener();
      if (!propsDelivered && lastProps && typeof l.applyUserProperties === 'function') {
        l.applyUserProperties(lastProps);
        propsDelivered = true;
        stats.propsApplied++;
      }
      if (lastGeneral && typeof l.applyGeneralProperties === 'function') l.applyGeneralProperties(lastGeneral);
      if (paused && typeof l.setPaused === 'function') l.setPaused(true);
    }, 1500);
  };
  if (document.readyState === 'complete') ready(); else window.addEventListener('load', ready, { once: true });
})();
