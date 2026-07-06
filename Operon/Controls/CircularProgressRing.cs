using System;
using System.Windows;
using System.Windows.Media;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Pen = System.Windows.Media.Pen;

namespace SystemActivityTracker.Controls
{
    // A small, dependency-free donut/ring gauge: a light "track" circle plus a colored
    // arc showing Progress (0-100). Used by the Monthly Usage summary so overall
    // progress toward Expected Hours reads at a glance instead of needing a
    // subtraction. Deliberately hand-drawn (no charting library) since the project's
    // only chart dependency (LiveCharts.Wpf 0.9.7) has no donut/pie series to lean on
    // here, and this keeps the visual fully within our own styling control.
    //
    // Type aliases below are required: this project also references System.Drawing
    // (via UseWindowsForms=true), which defines Point/Size/Brush/Brushes/Pen too —
    // without the aliases those names are ambiguous at compile time.
    public sealed class CircularProgressRing : FrameworkElement
    {
        public static readonly DependencyProperty ProgressProperty =
            DependencyProperty.Register(
                nameof(Progress),
                typeof(double),
                typeof(CircularProgressRing),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public double Progress
        {
            get => (double)GetValue(ProgressProperty);
            set => SetValue(ProgressProperty, value);
        }

        public static readonly DependencyProperty TrackBrushProperty =
            DependencyProperty.Register(
                nameof(TrackBrush),
                typeof(Brush),
                typeof(CircularProgressRing),
                new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush TrackBrush
        {
            get => (Brush)GetValue(TrackBrushProperty);
            set => SetValue(TrackBrushProperty, value);
        }

        public static readonly DependencyProperty ProgressBrushProperty =
            DependencyProperty.Register(
                nameof(ProgressBrush),
                typeof(Brush),
                typeof(CircularProgressRing),
                new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush ProgressBrush
        {
            get => (Brush)GetValue(ProgressBrushProperty);
            set => SetValue(ProgressBrushProperty, value);
        }

        public static readonly DependencyProperty RingThicknessProperty =
            DependencyProperty.Register(
                nameof(RingThickness),
                typeof(double),
                typeof(CircularProgressRing),
                new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public double RingThickness
        {
            get => (double)GetValue(RingThicknessProperty);
            set => SetValue(RingThicknessProperty, value);
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            double size = Math.Min(ActualWidth, ActualHeight);
            if (size <= 0)
            {
                return;
            }

            double thickness = Math.Min(RingThickness, size / 2);
            double radius = (size - thickness) / 2;
            var center = new Point(ActualWidth / 2, ActualHeight / 2);

            var trackPen = new Pen(TrackBrush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            drawingContext.DrawEllipse(null, trackPen, center, radius, radius);

            double fraction = Math.Clamp(Progress, 0.0, 100.0) / 100.0;
            if (fraction <= 0.0)
            {
                return;
            }

            var progressPen = new Pen(ProgressBrush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

            // A full 360° sweep degenerates (start == end point), so draw a complete
            // ellipse instead of an arc once progress reaches 100%.
            if (fraction >= 0.999)
            {
                drawingContext.DrawEllipse(null, progressPen, center, radius, radius);
                return;
            }

            double sweepDegrees = fraction * 360.0;
            var startPoint = PointOnCircle(center, radius, 0);
            var endPoint = PointOnCircle(center, radius, sweepDegrees);
            bool isLargeArc = sweepDegrees > 180.0;

            var figure = new PathFigure { StartPoint = startPoint, IsClosed = false };
            figure.Segments.Add(new ArcSegment(endPoint, new Size(radius, radius), 0, isLargeArc, SweepDirection.Clockwise, true));

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);

            drawingContext.DrawGeometry(null, progressPen, geometry);
        }

        // angleDegrees: 0 = top (12 o'clock), increasing clockwise.
        private static Point PointOnCircle(Point center, double radius, double angleDegrees)
        {
            double radians = angleDegrees * Math.PI / 180.0;
            return new Point(center.X + radius * Math.Sin(radians), center.Y - radius * Math.Cos(radians));
        }
    }
}
