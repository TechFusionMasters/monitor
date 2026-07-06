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
    // A donut split into up to three consecutive colored arcs (Value1, then Value2, then
    // Value3, clockwise from the top), proportional to their share of the total. Used by
    // the Monthly Usage "Total Active Hours" breakdown (Active / Offline / Leave) — unlike
    // CircularProgressRing this has no separate "remaining" track color, since the three
    // segments together always represent the whole circle.
    //
    // Same System.Drawing/System.Windows.Media name-collision aliases as
    // CircularProgressRing are required here (UseWindowsForms=true implicitly brings in
    // System.Drawing's Point/Size/Brush/Brushes/Pen).
    public sealed class SegmentedDonutRing : FrameworkElement
    {
        public static readonly DependencyProperty Value1Property =
            DependencyProperty.Register(nameof(Value1), typeof(double), typeof(SegmentedDonutRing),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public double Value1 { get => (double)GetValue(Value1Property); set => SetValue(Value1Property, value); }

        public static readonly DependencyProperty Value2Property =
            DependencyProperty.Register(nameof(Value2), typeof(double), typeof(SegmentedDonutRing),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public double Value2 { get => (double)GetValue(Value2Property); set => SetValue(Value2Property, value); }

        public static readonly DependencyProperty Value3Property =
            DependencyProperty.Register(nameof(Value3), typeof(double), typeof(SegmentedDonutRing),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public double Value3 { get => (double)GetValue(Value3Property); set => SetValue(Value3Property, value); }

        public static readonly DependencyProperty Brush1Property =
            DependencyProperty.Register(nameof(Brush1), typeof(Brush), typeof(SegmentedDonutRing),
                new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));
        public Brush Brush1 { get => (Brush)GetValue(Brush1Property); set => SetValue(Brush1Property, value); }

        public static readonly DependencyProperty Brush2Property =
            DependencyProperty.Register(nameof(Brush2), typeof(Brush), typeof(SegmentedDonutRing),
                new FrameworkPropertyMetadata(Brushes.Orange, FrameworkPropertyMetadataOptions.AffectsRender));
        public Brush Brush2 { get => (Brush)GetValue(Brush2Property); set => SetValue(Brush2Property, value); }

        public static readonly DependencyProperty Brush3Property =
            DependencyProperty.Register(nameof(Brush3), typeof(Brush), typeof(SegmentedDonutRing),
                new FrameworkPropertyMetadata(Brushes.MediumPurple, FrameworkPropertyMetadataOptions.AffectsRender));
        public Brush Brush3 { get => (Brush)GetValue(Brush3Property); set => SetValue(Brush3Property, value); }

        public static readonly DependencyProperty TrackBrushProperty =
            DependencyProperty.Register(nameof(TrackBrush), typeof(Brush), typeof(SegmentedDonutRing),
                new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));
        public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }

        public static readonly DependencyProperty RingThicknessProperty =
            DependencyProperty.Register(nameof(RingThickness), typeof(double), typeof(SegmentedDonutRing),
                new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public double RingThickness { get => (double)GetValue(RingThicknessProperty); set => SetValue(RingThicknessProperty, value); }

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

            // Faint base track — visible whenever the three values don't add up to
            // anything (all zero), and as a subtle backdrop otherwise.
            var trackPen = new Pen(TrackBrush, thickness);
            drawingContext.DrawEllipse(null, trackPen, center, radius, radius);

            double v1 = Math.Max(0, Value1);
            double v2 = Math.Max(0, Value2);
            double v3 = Math.Max(0, Value3);
            double total = v1 + v2 + v3;
            if (total <= 0)
            {
                return;
            }

            double angle = 0;
            angle = DrawSegment(drawingContext, center, radius, thickness, angle, v1 / total * 360.0, Brush1);
            angle = DrawSegment(drawingContext, center, radius, thickness, angle, v2 / total * 360.0, Brush2);
            DrawSegment(drawingContext, center, radius, thickness, angle, v3 / total * 360.0, Brush3);
        }

        // Draws one arc from startAngleDegrees through startAngleDegrees + sweepDegrees
        // (clockwise from 12 o'clock) and returns the new current angle.
        private static double DrawSegment(DrawingContext dc, Point center, double radius, double thickness, double startAngleDegrees, double sweepDegrees, Brush brush)
        {
            if (sweepDegrees <= 0.01)
            {
                return startAngleDegrees;
            }

            double endAngleDegrees = startAngleDegrees + sweepDegrees;
            var pen = new Pen(brush, thickness);

            // A segment that is (essentially) the whole circle degenerates as an arc
            // (start == end point), so draw a full ellipse instead.
            if (sweepDegrees >= 359.99)
            {
                dc.DrawEllipse(null, pen, center, radius, radius);
                return endAngleDegrees;
            }

            var startPoint = PointOnCircle(center, radius, startAngleDegrees);
            var endPoint = PointOnCircle(center, radius, endAngleDegrees);
            bool isLargeArc = sweepDegrees > 180.0;

            var figure = new PathFigure { StartPoint = startPoint, IsClosed = false };
            figure.Segments.Add(new ArcSegment(endPoint, new Size(radius, radius), 0, isLargeArc, SweepDirection.Clockwise, true));

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);

            dc.DrawGeometry(null, pen, geometry);
            return endAngleDegrees;
        }

        // angleDegrees: 0 = top (12 o'clock), increasing clockwise.
        private static Point PointOnCircle(Point center, double radius, double angleDegrees)
        {
            double radians = angleDegrees * Math.PI / 180.0;
            return new Point(center.X + radius * Math.Sin(radians), center.Y - radius * Math.Cos(radians));
        }
    }
}
