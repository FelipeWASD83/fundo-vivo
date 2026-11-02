using System.Runtime.InteropServices;

namespace FundoVivo.Engine;

/// <summary>
/// Listens to what the PC is playing (WASAPI loopback, straight from Windows, no extra library) and turns it into
/// 64 bands per channel, about 30 times a second. Runs only while a web wallpaper asked for audio.
/// </summary>
public sealed class AudioSpectrum : IDisposable
{
    public const int Bands = 64;
    const int FftSize = 2048;

    readonly Action<float[]> _onBands;
    Thread _thread;
    volatile bool _running;

    public bool Running => _running;

    public AudioSpectrum(Action<float[]> onBands) { _onBands = onBands; }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "audio", Priority = ThreadPriority.BelowNormal };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        _thread?.Join(1000);
        _thread = null;
    }

    public void Dispose() => Stop();

    void Loop()
    {
        while (_running)
        {
            try { Capture(); }
            catch (Exception e)
            {
                // Device unplugged or default output changed: start over on the new default.
                Log.Write("audio capture: " + e.Message);
                for (int i = 0; i < 20 && _running; i++) Thread.Sleep(100);
            }
        }
    }

    unsafe void Capture()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        enumerator.GetDefaultAudioEndpoint(0 /* render */, 1 /* multimedia */, out IMMDevice device);
        device.GetId(out string deviceId);
        Guid iidClient = typeof(IAudioClient).GUID;
        device.Activate(ref iidClient, 0x17 /* CLSCTX_ALL */, IntPtr.Zero, out object clientObj);
        var client = (IAudioClient)clientObj;
        client.GetMixFormat(out IntPtr formatPtr);
        try
        {
            var fmt = Marshal.PtrToStructure<WaveFormatEx>(formatPtr);
            bool isFloat = fmt.wFormatTag == 3 || (fmt.wFormatTag == 0xFFFE && fmt.wBitsPerSample == 32 && IsFloatSubtype(formatPtr));
            int channels = fmt.nChannels, bytesPerSample = fmt.wBitsPerSample / 8;
            const int LOOPBACK = 0x00020000;
            client.Initialize(0 /* shared */, LOOPBACK, 2_000_000 /* 200 ms */, 0, formatPtr, IntPtr.Zero);
            Guid iidCapture = typeof(IAudioCaptureClient).GUID;
            client.GetService(ref iidCapture, out object captureObj);
            var capture = (IAudioCaptureClient)captureObj;

            var ringL = new float[FftSize];
            var ringR = new float[FftSize];
            var left = new float[FftSize];
            var right = new float[FftSize];
            int pos = 0;
            var analyzer = new SpectrumAnalyzer(FftSize, fmt.nSamplesPerSec);
            long lastSend = 0, lastPacket = 0, lastDeviceCheck = 0;
            bool cleared = false;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            client.Start();
            try
            {
                while (_running)
                {
                    Thread.Sleep(10);
                    capture.GetNextPacketSize(out int packet);
                    if (packet > 0) { lastPacket = clock.ElapsedMilliseconds; cleared = false; }
                    else if (!cleared && clock.ElapsedMilliseconds - lastPacket > 60)
                    {
                        // Loopback sends nothing while the PC is silent: drop the last sound instead of repeating it.
                        Array.Clear(ringL);
                        Array.Clear(ringR);
                        cleared = true;
                    }
                    if (clock.ElapsedMilliseconds - lastDeviceCheck > 2000)
                    {
                        // Loopback stays on the old device when the default output changes; start over on the new one.
                        lastDeviceCheck = clock.ElapsedMilliseconds;
                        enumerator.GetDefaultAudioEndpoint(0, 1, out IMMDevice current);
                        current.GetId(out string currentId);
                        Marshal.ReleaseComObject(current);
                        if (currentId != deviceId) { Log.Write("default audio output changed"); break; }
                    }
                    while (packet > 0)
                    {
                        capture.GetBuffer(out IntPtr data, out int frames, out int flags, out _, out _);
                        bool silent = (flags & 0x2) != 0;
                        for (int f = 0; f < frames; f++)
                        {
                            float l = 0, r = 0;
                            if (!silent)
                            {
                                byte* frame = (byte*)data + f * channels * bytesPerSample;
                                l = Sample(frame, 0, isFloat, bytesPerSample);
                                r = channels > 1 ? Sample(frame, 1, isFloat, bytesPerSample) : l;
                            }
                            ringL[pos] = l;
                            ringR[pos] = r;
                            pos = (pos + 1) % FftSize;
                        }
                        capture.ReleaseBuffer(frames);
                        capture.GetNextPacketSize(out packet);
                    }
                    // Deadlines accumulate, so the coarse Windows sleep (about 15.6 ms) still averages 30 per second.
                    long now = clock.ElapsedMilliseconds;
                    if (now >= lastSend)
                    {
                        lastSend = Math.Max(lastSend + 33, now - 33);
                        // Unroll the ring so the oldest sample comes first.
                        Array.Copy(ringL, pos, left, 0, FftSize - pos); Array.Copy(ringL, 0, left, FftSize - pos, pos);
                        Array.Copy(ringR, pos, right, 0, FftSize - pos); Array.Copy(ringR, 0, right, FftSize - pos, pos);
                        var bands = new float[Bands * 2];
                        analyzer.Analyze(left, bands, 0);
                        analyzer.Analyze(right, bands, Bands);
                        _onBands(bands);
                    }
                }
            }
            finally { client.Stop(); Marshal.ReleaseComObject(capture); }
        }
        finally
        {
            Marshal.FreeCoTaskMem(formatPtr);
            Marshal.ReleaseComObject(client);
            Marshal.ReleaseComObject(device);
            Marshal.ReleaseComObject(enumerator);
        }
    }

    static unsafe float Sample(byte* frame, int channel, bool isFloat, int bytes)
    {
        byte* p = frame + channel * bytes;
        if (isFloat) return *(float*)p;
        return bytes switch
        {
            2 => *(short*)p / 32768f,
            3 => ((p[0] << 8 | p[1] << 16 | p[2] << 24) >> 8) / 8388608f,
            4 => *(int*)p / 2147483648f,
            _ => 0,
        };
    }

    static bool IsFloatSubtype(IntPtr extensible)
    {
        // WAVEFORMATEXTENSIBLE: the SubFormat GUID sits after the 18-byte header and 6 bytes of extra fields.
        var sub = Marshal.PtrToStructure<Guid>(extensible + 24);
        return sub == new Guid("00000003-0000-0010-8000-00aa00389b71");
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    struct WaveFormatEx { public ushort wFormatTag, nChannels; public int nSamplesPerSec, nAvgBytesPerSec; public ushort nBlockAlign, wBitsPerSample, cbSize; }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints(int dataFlow, int mask, out IntPtr devices);
        void GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        void Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        void OpenPropertyStore(int access, out IntPtr store);
        void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioClient
    {
        void Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
        void GetBufferSize(out int frames);
        void GetStreamLatency(out long latency);
        void GetCurrentPadding(out int padding);
        void IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
        void GetMixFormat(out IntPtr format);
        void GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        void Start();
        void Stop();
        void Reset();
        void SetEventHandle(IntPtr handle);
        void GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioCaptureClient
    {
        void GetBuffer(out IntPtr data, out int frames, out int flags, out long devicePosition, out long qpcPosition);
        void ReleaseBuffer(int frames);
        void GetNextPacketSize(out int frames);
    }
}

/// <summary>Hann window, radix-2 FFT, log-spaced bands with fast attack and slower decay.</summary>
public sealed class SpectrumAnalyzer
{
    readonly int _n;
    readonly float[] _window, _re, _im;
    readonly int[] _bandStart, _bandEnd;
    readonly float[][] _smooth = { new float[AudioSpectrum.Bands], new float[AudioSpectrum.Bands] };

    public SpectrumAnalyzer(int n, int sampleRate)
    {
        _n = n;
        _window = new float[n];
        for (int i = 0; i < n; i++) _window[i] = 0.5f * (1 - MathF.Cos(2 * MathF.PI * i / (n - 1)));
        _re = new float[n];
        _im = new float[n];
        _bandStart = new int[AudioSpectrum.Bands];
        _bandEnd = new int[AudioSpectrum.Bands];
        double lo = 30, hi = Math.Min(16000, sampleRate / 2.0);
        double binHz = (double)sampleRate / n;
        for (int b = 0; b < AudioSpectrum.Bands; b++)
        {
            double f0 = lo * Math.Pow(hi / lo, (double)b / AudioSpectrum.Bands);
            double f1 = lo * Math.Pow(hi / lo, (double)(b + 1) / AudioSpectrum.Bands);
            _bandStart[b] = Math.Max(1, (int)(f0 / binHz));
            _bandEnd[b] = Math.Max(_bandStart[b] + 1, (int)Math.Ceiling(f1 / binHz));
        }
    }

    public void Analyze(float[] samples, float[] output, int offset)
    {
        for (int i = 0; i < _n; i++) { _re[i] = samples[i] * _window[i]; _im[i] = 0; }
        Fft(_re, _im);
        var smooth = _smooth[offset == 0 ? 0 : 1];
        for (int b = 0; b < AudioSpectrum.Bands; b++)
        {
            float peak = 0;
            for (int k = _bandStart[b]; k < _bandEnd[b] && k < _n / 2; k++)
            {
                float mag = MathF.Sqrt(_re[k] * _re[k] + _im[k] * _im[k]) * 4f / _n;
                if (mag > peak) peak = mag;
            }
            // Roughly 0..1 for normal listening levels; treble gets a gentle lift so the bands look even.
            float tilt = 1f + 1.5f * b / AudioSpectrum.Bands;
            float v = MathF.Min(1.5f, MathF.Sqrt(peak) * 1.6f * tilt);
            smooth[b] = v > smooth[b] ? v : smooth[b] * 0.78f + v * 0.22f;
            output[offset + b] = smooth[b];
        }
    }

    static void Fft(float[] re, float[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            float ang = -2 * MathF.PI / len;
            float wr = MathF.Cos(ang), wi = MathF.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                float cr = 1, ci = 0;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = a + len / 2;
                    float tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - tr; im[b] = im[a] - ti;
                    re[a] += tr; im[a] += ti;
                    float ncr = cr * wr - ci * wi;
                    ci = cr * wi + ci * wr;
                    cr = ncr;
                }
            }
        }
    }
}
