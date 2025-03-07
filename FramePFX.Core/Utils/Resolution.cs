using System;
using System.Collections.Generic;
using System.Numerics;

namespace FramePFX.Core.Utils {
    public readonly struct Resolution : IComparable<Resolution>, IEqualityComparer<Resolution> {
        /// <summary>
        /// A resolution whose width and height is zero
        /// </summary>
        public static readonly Resolution Empty = new Resolution(0, 0);

        /// <summary>
        /// The resolution's width
        /// </summary>
        public int Width { get; }

        /// <summary>
        /// The resolution's height
        /// </summary>
        public int Height { get; }

        /// <summary>
        /// Whether this resolution's width and height is zero or not
        /// </summary>
        public bool IsEmpty => this.Width == 0 && this.Height == 0;

        public Resolution(int width, int height) {
            this.Width = width;
            this.Height = height;
        }

        public bool Equals(Resolution other) {
            return this.Width == other.Width && this.Height == other.Height;
        }

        public override bool Equals(object obj) {
            return obj is Resolution other && this.Equals(other);
        }

        public override int GetHashCode() {
            unchecked {
                return (this.Width * 397) ^ this.Height;
            }
        }

        public int CompareTo(Resolution other) {
            int cmp = this.Width.CompareTo(other.Width);
            if (cmp == 0)
                cmp = this.Height.CompareTo(other.Height);
            return cmp;
        }

        public bool Equals(Resolution x, Resolution y) {
            return x.Equals(y);
        }

        public int GetHashCode(Resolution obj) {
            return obj.GetHashCode();
        }

        public static explicit operator Vector2(Resolution res) {
            return new Vector2(res.Width, res.Height);
        }

        public static explicit operator Resolution(Vector2 res) {
            return new Resolution((int) Math.Floor(res.X), (int) Math.Floor(res.Y));
        }

        public static explicit operator Resolution(ulong res) => new Resolution((int) (res >> 32), (int) (res & uint.MaxValue));

        public static explicit operator ulong(Resolution res) => ((ulong) res.Width << 32) | (uint) res.Height;

        public Resolution WithWidth(int width) {
            return new Resolution(width, this.Height);
        }

        public Resolution WithHeight(int height) {
            return new Resolution(this.Width, height);
        }

        public override string ToString() {
            return $"{this.Width}x{this.Height}";
        }
    }
}