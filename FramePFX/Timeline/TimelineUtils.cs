﻿using System;

namespace FramePFX.Timeline {
    public static class TimelineUtils {
        public const double MinUnitZoom = 0.0001d;

        public static object ClampUnitZoom(object width) {
            return (double) width < MinUnitZoom ? MinUnitZoom : width;
        }

        public static bool IsZoomEqual(double a, double b) {
            return Math.Abs(a - b) <= MinUnitZoom;
        }
    }
}
