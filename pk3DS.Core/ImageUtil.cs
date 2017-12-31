﻿using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using pk3DS.Core.CTR;

namespace pk3DS.Core
{
    /// <summary>
    /// Image Utility class using <see cref="System.Drawing"/>.
    /// </summary>
    public static class ImageUtil
    {
        /// <summary>
        /// Converts a <see cref="BFLIM"/> to <see cref="Bitmap"/> via the 32bit/pixel data.
        /// </summary>
        /// <param name="bflim">Image data</param>
        /// <param name="crop">Crop the image area to the actual dimensions</param>
        /// <returns>Human visible data</returns>
        public static Bitmap GetBitmap(this BFLIM bflim, bool crop = true)
        {
            if (bflim.Format == BFLIMEncoding.ETC1 || bflim.Format == BFLIMEncoding.ETC1A4)
                return GetBitmapETC(bflim);
            var data = bflim.GetImageData(crop);
            return GetBitmap(data, bflim.Footer.Width, bflim.Footer.Height);
        }
        public static Bitmap GetBitmap(byte[] data, int width, int height)
        {
            var bmp = new Bitmap(width, height);
            var rect = new Rectangle(0, 0, width, height);
            var bmpData = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);

            var ptr = bmpData.Scan0;
            Marshal.Copy(data, 0, ptr, data.Length);
            bmp.UnlockBits(bmpData);

            return bmp;
        }

        public static Bitmap GetBitmapETC(BCLIM.CLIM bclim)
        {
            ETC1.CheckETC1Lib();

            Bitmap img = new Bitmap(Math.Max(BCLIM.nlpo2(bclim.Width), 16), Math.Max(BCLIM.nlpo2(bclim.Height), 16));
            try { return DecodeETC(bclim, img, bclim.Data, bclim.FileFormat == 0x0B); }
            catch { return img; }
        }
        public static Bitmap GetBitmapETC(BFLIM bflim)
        {
            ETC1.CheckETC1Lib();

            Bitmap img = new Bitmap(Math.Max(BCLIM.nlpo2(bflim.Width), 16), Math.Max(BCLIM.nlpo2(bflim.Height), 16));
            try { return DecodeETC(bflim, img, bflim.PixelData, bflim.Footer.Format == BFLIMEncoding.ETC1A4); }
            catch { return img; }
        }

        private static Bitmap DecodeETC(IXLIM bclim, Bitmap img, byte[] textureData, bool etc1A4)
        {
            /* http://jul.rustedlogic.net/thread.php?id=17312
             * Much of this code is taken/modified from Tharsis. Thank you to Tharsis's creator, xdaniel.
             * https://github.com/xdanieldzd/Tharsis
             */

            /* Get compressed data & handle to it */

            //textureData = switchEndianness(textureData, 0x10);
            ushort[] input = new ushort[textureData.Length / sizeof(ushort)];
            Buffer.BlockCopy(textureData, 0, input, 0, textureData.Length);
            GCHandle pInput = GCHandle.Alloc(input, GCHandleType.Pinned);

            /* Marshal data around, invoke ETC1.dll for conversion, etc */
            uint size1 = 0;
            var w = (ushort)img.Width;
            var h = (ushort)img.Height;

            ETC1.ConvertETC1(IntPtr.Zero, ref size1, IntPtr.Zero, w, h, etc1A4); // true = etc1a4, false = etc1
            uint[] output = new uint[size1];
            GCHandle pOutput = GCHandle.Alloc(output, GCHandleType.Pinned);
            ETC1.ConvertETC1(pOutput.AddrOfPinnedObject(), ref size1, pInput.AddrOfPinnedObject(), w, h, etc1A4);
            pOutput.Free();
            pInput.Free();

            /* Unscramble if needed // could probably be done in ETC1Lib.dll, it's probably pretty ugly, but whatever... */
            /* Non-square code blocks could need some cleanup, verification, etc. as well... */
            uint[] finalized = new uint[output.Length];

            // Act if it's square because BCLIM swizzling is stupid
            Buffer.BlockCopy(output, 0, finalized, 0, finalized.Length);

            byte[] tmp = new byte[finalized.Length];
            Buffer.BlockCopy(finalized, 0, tmp, 0, tmp.Length);
            byte[] imgData = tmp;

            for (int i = 0; i < w; i++)
            {
                for (int j = 0; j < h; j++)
                {
                    int k = (j + i * img.Height) * 4;
                    img.SetPixel(i, j, Color.FromArgb(imgData[k + 3], imgData[k], imgData[k + 1], imgData[k + 2]));
                }
            }
            // Image is 13  instead of 12
            //          24             34
            img.RotateFlip(RotateFlipType.Rotate90FlipX);
            if (w > h)
            {
                // Image is now in appropriate order, but the shifting is messed up. Let's fix that.
                Bitmap img2 = new Bitmap(Math.Max(BCLIM.nlpo2(bclim.Width), 16), Math.Max(BCLIM.nlpo2(bclim.Height), 16));
                for (int y = 0; y < Math.Max(BCLIM.nlpo2(bclim.Width), 16); y += 8)
                {
                    for (int x = 0; x < Math.Max(BCLIM.nlpo2(bclim.Height), 16); x++)
                    {
                        for (int j = 0; j < 8; j++)
                        // Treat every 8 vertical pixels as 1 pixel for purposes of calculation, add to offset later.
                        {
                            int x1 = (x + y / 8 * h) % img2.Width; // Reshift x
                            int y1 = (x + y / 8 * h) / img2.Width * 8; // Reshift y
                            img2.SetPixel(x1, y1 + j, img.GetPixel(x, y + j)); // Reswizzle
                        }
                    }
                }
                img = img2;
            }
            else if (h > w)
            {
                Bitmap img2 = new Bitmap(Math.Max(BCLIM.nlpo2(bclim.Width), 16), Math.Max(BCLIM.nlpo2(bclim.Height), 16));
                for (int y = 0; y < Math.Max(BCLIM.nlpo2(bclim.Width), 16); y += 8)
                {
                    for (int x = 0; x < Math.Max(BCLIM.nlpo2(bclim.Height), 16); x++)
                    {
                        for (int j = 0; j < 8; j++)
                        // Treat every 8 vertical pixels as 1 pixel for purposes of calculation, add to offset later.
                        {
                            int x1 = x % img2.Width; // Reshift x
                            int y1 = (x + y / 8 * h) / img2.Width * 8; // Reshift y
                            img2.SetPixel(x1, y1 + j, img.GetPixel(x, y + j)); // Reswizzle
                        }
                    }
                }
                img = img2;
            }

            return img;
        }
    }
}