using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SystemActivityTracker.Controls
{
    // Plays a small looping GIF from the app's Assets folder (copied next to the exe at
    // build/publish time — see Operon.csproj). Set GifKey to a file name without
    // extension (e.g. "TowardsTarget" for Assets/TowardsTarget.gif); set it to null or
    // empty to hide the control. Stretch is fixed to Uniform so it always fits its
    // allotted space without distortion or overflow, however small the host tile is.
    // Fully qualified: this project also references System.Windows.Forms
    // (UseWindowsForms=true), whose implicit usings bring in System.Drawing.Image too,
    // making the bare name "Image" ambiguous.
    public sealed class AnimatedGifImage : System.Windows.Controls.Image
    {
        public static readonly DependencyProperty GifKeyProperty =
            DependencyProperty.Register(
                nameof(GifKey),
                typeof(string),
                typeof(AnimatedGifImage),
                new PropertyMetadata(null, OnGifKeyChanged));

        public string? GifKey
        {
            get => (string?)GetValue(GifKeyProperty);
            set => SetValue(GifKeyProperty, value);
        }

        private BitmapSource[] _frames = Array.Empty<BitmapSource>();
        private int[] _frameDelaysMs = Array.Empty<int>();
        private int _frameIndex;
        private DispatcherTimer? _timer;

        public AnimatedGifImage()
        {
            Stretch = Stretch.Uniform;
            Visibility = Visibility.Collapsed;
            Loaded += (_, __) => StartAnimation();
            Unloaded += (_, __) => StopAnimation();
        }

        private static void OnGifKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is AnimatedGifImage control)
            {
                control.LoadGif(e.NewValue as string);
            }
        }

        private void LoadGif(string? key)
        {
            StopAnimation();

            if (string.IsNullOrWhiteSpace(key))
            {
                Source = null;
                Visibility = Visibility.Collapsed;
                _frames = Array.Empty<BitmapSource>();
                _frameDelaysMs = Array.Empty<int>();
                return;
            }

            (_frames, _frameDelaysMs) = GifFrameCache.GetFrames(key);
            _frameIndex = 0;

            if (_frames.Length == 0)
            {
                Source = null;
                Visibility = Visibility.Collapsed;
                return;
            }

            Source = _frames[0];
            Visibility = Visibility.Visible;

            if (IsLoaded)
            {
                StartAnimation();
            }
        }

        private void StartAnimation()
        {
            if (_frames.Length <= 1)
            {
                return;
            }

            StopAnimation();
            _timer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(GetDelay(0))
            };
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (_frames.Length == 0)
            {
                return;
            }

            _frameIndex = (_frameIndex + 1) % _frames.Length;
            Source = _frames[_frameIndex];

            if (_timer != null)
            {
                _timer.Interval = TimeSpan.FromMilliseconds(GetDelay(_frameIndex));
            }
        }

        private int GetDelay(int index) =>
            index < _frameDelaysMs.Length && _frameDelaysMs[index] > 0 ? _frameDelaysMs[index] : 100;

        private void StopAnimation()
        {
            if (_timer == null)
            {
                return;
            }

            _timer.Stop();
            _timer.Tick -= Timer_Tick;
            _timer = null;
        }
    }

    // Decodes each Assets/{key}.gif once and shares the frames across every control
    // instance requesting the same key — a month view can have several tiles showing
    // the same GIF simultaneously, and this avoids re-decoding it for each one.
    internal static class GifFrameCache
    {
        private static readonly Dictionary<string, (BitmapSource[] Frames, int[] DelaysMs)> Cache =
            new Dictionary<string, (BitmapSource[] Frames, int[] DelaysMs)>(StringComparer.OrdinalIgnoreCase);

        public static (BitmapSource[] Frames, int[] DelaysMs) GetFrames(string key)
        {
            if (Cache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var result = Load(key);
            Cache[key] = result;
            return result;
        }

        private static (BitmapSource[] Frames, int[] DelaysMs) Load(string key)
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Assets", $"{key}.gif");
                if (!File.Exists(path))
                {
                    return (Array.Empty<BitmapSource>(), Array.Empty<int>());
                }

                var decoder = new GifBitmapDecoder(
                    new Uri(path, UriKind.Absolute),
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);

                var frames = new BitmapSource[decoder.Frames.Count];
                var delays = new int[decoder.Frames.Count];

                for (int i = 0; i < decoder.Frames.Count; i++)
                {
                    var frame = decoder.Frames[i];
                    if (frame.CanFreeze)
                    {
                        frame.Freeze();
                    }

                    frames[i] = frame;
                    delays[i] = GetFrameDelayMs(frame);
                }

                return (frames, delays);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AnimatedGifImage] Failed to load '{key}': {ex}");
                return (Array.Empty<BitmapSource>(), Array.Empty<int>());
            }
        }

        private static int GetFrameDelayMs(BitmapFrame frame)
        {
            try
            {
                if (frame.Metadata is BitmapMetadata metadata)
                {
                    var query = metadata.GetQuery("/grctlext/Delay");
                    if (query is ushort hundredths && hundredths > 0)
                    {
                        return hundredths * 10;
                    }
                }
            }
            catch
            {
                // Some GIFs lack this metadata chunk — fall back to the default below.
            }

            return 100;
        }
    }
}
