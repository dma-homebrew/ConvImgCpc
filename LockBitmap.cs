﻿using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ConvImgCpc {
	public class LockBitmap {
		private Bitmap source = null;
		private IntPtr Iptr = IntPtr.Zero;
		BitmapData bitmapData = null;

		public byte[] Pixels { get; set; }
		public int Depth { get; private set; }
		public int Width { get; private set; }
		public int Height { get; private set; }

		public LockBitmap(Bitmap source) {
			this.source = source;
			Width = source.Width;
			Height = source.Height;
			Depth = Bitmap.GetPixelFormatSize(source.PixelFormat);
			Pixels = new byte[Width * Height * (Depth / 8)];
		}

		public void LockBits() {
			bitmapData = source.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppRgb);
			Iptr = bitmapData.Scan0;
			Marshal.Copy(Iptr, Pixels, 0, Pixels.Length);
		}

		public void UnlockBits() {
			Marshal.Copy(Pixels, 0, Iptr, Pixels.Length);
			source.UnlockBits(bitmapData);
		}

		public int GetPixel(int pixelX, int pixelY) {
			int adr = ((pixelY * Width) + pixelX) << 2;
			return Pixels[adr] + (Pixels[adr + 1] << 8) + (Pixels[adr + 2] << 16);
		}

		public RvbColor GetPixelColor(int pixelX, int pixelY) {
			int adr = ((pixelY * Width) + pixelX) << 2;
			return new RvbColor(Pixels[adr] + (Pixels[adr + 1] << 8) + (Pixels[adr + 2] << 16));
		}

		public void SetPixel(int pixelX, int pixelY, int color) {
			int adr = ((pixelY * Width) + pixelX) << 2;
			Pixels[adr++] = (byte)(color);
			Pixels[adr++] = (byte)(color >> 8);
			Pixels[adr++] = (byte)(color >> 16);
			Pixels[adr] = 0xFF;
		}

		public void SetPixel(int pixelX, int pixelY, RvbColor color) {
			int adr = ((pixelY * Width) + pixelX) << 2;
			Pixels[adr++] = color.red;
			Pixels[adr++] = color.green;
			Pixels[adr++] = color.blue;
			Pixels[adr] = 0xFF;
		}
	}

	public class RvbColor {
		public byte red, green, blue;

		public RvbColor(byte compR, byte compV, byte compB) {
			red = compR;
			green = compV;
			blue = compB;
		}

		public RvbColor(int value) {
			red = (byte)value;
			green = (byte)(value >> 8);
			blue = (byte)(value >> 16);
		}

		public int GetColor { get { return red + (green << 8) + (blue << 16); } }
		public int GetColorArgb { get { return red + (green << 8) + (blue << 16) + (255 << 24); } }
	}
}