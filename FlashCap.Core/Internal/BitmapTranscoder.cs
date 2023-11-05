////////////////////////////////////////////////////////////////////////////
//
// FlashCap - Independent camera capture library.
// Copyright (c) Kouji Matsui (@kozy_kekyo, @kekyo@mastodon.cloud)
//
// Licensed under Apache-v2: https://opensource.org/licenses/Apache-2.0
//
////////////////////////////////////////////////////////////////////////////

using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace FlashCap.Internal;

public enum YUV2RGBConversionStandard
{
    Auto,
    BT_601,
    BT_709,
    BT_2020,
}
internal static class BitmapTranscoder
{
    private static readonly int scatteringBase = Environment.ProcessorCount;

    // Prefered article: https://docs.microsoft.com/en-us/windows/win32/medfound/recommended-8-bit-yuv-formats-for-video-rendering#420-formats-16-bits-per-pixel

    // some interesting code references:
    // https://chromium.googlesource.com/libyuv/libyuv/+/HEAD/unit_test/color_test.cc
    // performFullRange = true means that we suppose Y is in [16..235] range and UV in [16..240]
    // performFullRange = false means that we suppose Y U and V are in [0..255] range
    private static unsafe void TranscodeFromYUVInternal(
       int width, int height, 
       YUV2RGBConversionStandard conversionStandard,
       bool performFullRange, bool isUYVY,
       byte* pFrom, byte* pTo)
    {
        // default values BT.601
        int multY = 255;
        int multUB = 516;
        int multUG = 100;
        int multVG = 208;
        int multVR = 409;
        int offsetY = 0;

        // set constants for the color conversion
        switch (conversionStandard)
        {
            case YUV2RGBConversionStandard.BT_601:
                //Color profile ITU-R BT.601 Limited Range
                //matrix 1.0,    1.0,     1.0,
                //       0.0,   -0.39173, 2.0170,
                //       1.5958,-0.81290, 0.0
                //converts to rounded int (multiply by 256)
                //256,  256, 256,
                //0   ,-100, 516
                //409, -208, 0

                if (performFullRange)
                {
                    // YUV limited range
                    // (Y  is in [16..235], rescale to [0..255])
                    // (UV is in [16..240], rescale to [0..255])

                    // multiply  Y by 1.16438
                    // multiply UV by 1.13839
                    multY = 298;
                    multUB = 587;
                    multUG = 114;
                    multVG = 237;
                    multVR = 466;
                    offsetY = 16;
                }
                break;
            case YUV2RGBConversionStandard.BT_709:
                //Color profile ITU-R BT.709 Limited Range
                //matrix 1.0,     1.0,    1.0,
                //       0.0,    -0.1873, 1.8556,
                //       1.5748, -0.4681, 0.0
                // converts to rounded int (multiply by 256)
                //256, 256, 256,
                //0   ,-48, 475
                //403,-120, 0

                if (performFullRange)
                {
                    // YUV limited range
                    // (Y  is in [16..235], rescale to [0..255])
                    // (UV is in [16..240], rescale to [0..255])

                    // multiply  Y by 1.16438
                    // multiply UV by 1.13839
                    multY = 298;
                    multUB = 541;
                    multUG = 55;
                    multVG = 137;
                    multVR = 459;
                    offsetY = 16;
                }
                else
                {
                    multY = 298;
                    multUB = 475;
                    multUG = 48;
                    multVG = 120;
                    multVR = 403;
                    offsetY = 0;
                }
                break;
            case YUV2RGBConversionStandard.BT_2020:
                if (performFullRange)
                {
                    // YUV limited range
                    // (Y  is in [16..235], rescale to [0..255])
                    // (UV is in [16..240], rescale to [0..255])

                    // multiply  Y by 1.16438
                    // multiply UV by 1.13839
                    multY = 298;
                    multUB = 549;
                    multUG = 48;
                    multVG = 166;
                    multVR = 429;
                    offsetY = 16;
              }
                else
                {
                    multY = 298;
                    multUB = 482;
                    multUG = 42;
                    multVG = 146;
                    multVR = 377;
                    offsetY = 0;
                }
                break;
        }
        var scatter = height / scatteringBase;
        Parallel.For(0, (height + scatter - 1) / scatter, ys =>
        {
            var y = ys * scatter;
            var myi = Math.Min(height - y, scatter);
            int c1, c2, d, e, cc1, cc2;

            for (var yi = 0; yi < myi; yi++)
            {
                byte* pFromBase = pFrom + (height - (y + yi) - 1) * width * 2;
                byte* pToBase = pTo + (y + yi) * width * 3;

                for (var x = 0; x < width; x += 2)
                {
                    if (isUYVY)
                    {
                        d = pFromBase[0] - 128;  // U
                        c1 = pFromBase[1] - offsetY;  // Y1
                        e = pFromBase[2] - 128;  // V
                        c2 = pFromBase[3] - offsetY;  // Y2
                    }
                    else
                    {
                        c1 = pFromBase[0] - offsetY;   // Y1
                        d = pFromBase[1] - 128;   // U
                        c2 = pFromBase[2] - offsetY;   // Y2
                        e = pFromBase[3] - 128;   // V
                    }

                    cc1 = multY * c1;
                    cc2 = multY * c2;

                    *pToBase++ = Clip((cc1 + multUB * d + 128) >> 8);   // B1
                    *pToBase++ = Clip((cc1 - multUG * d - multVG * e + 128) >> 8);   // G1
                    *pToBase++ = Clip((cc1 + multVR * e + 128) >> 8);   // R1

                    *pToBase++ = Clip((cc2 + multUB * d + 128) >> 8);   // B2
                    *pToBase++ = Clip((cc2 - multUG * d - multVG * e + 128) >> 8);   // G2
                    *pToBase++ = Clip((cc2 + multVR * e + 128) >> 8);   // R2

                    pFromBase += 4;
                }
            }
        });
    }

    private static unsafe void TranscodeFromYUV(
        int width, int height,
        YUV2RGBConversionStandard conversionStandard,
        bool isUYVY,
        bool performFullRange,
       byte* pFrom, byte* pTo)
    {
        switch (conversionStandard)
        {
            case YUV2RGBConversionStandard.BT_601:
            case YUV2RGBConversionStandard.BT_709:
            case YUV2RGBConversionStandard.BT_2020:
                TranscodeFromYUVInternal(width, height, conversionStandard, performFullRange, isUYVY, pFrom, pTo);
                break;
            case YUV2RGBConversionStandard.Auto:
                // determine the color conversion based on the width and height of the frame
                if (width > 1920 || height > 1080)  // UHD or larger
                    TranscodeFromYUVInternal(width, height, YUV2RGBConversionStandard.BT_2020, performFullRange, isUYVY, pFrom, pTo);
                else if (width > 720 || height > 576) // HD
                    TranscodeFromYUVInternal(width, height, YUV2RGBConversionStandard.BT_709, performFullRange, isUYVY, pFrom, pTo);
                else // SD
                    TranscodeFromYUVInternal(width, height, YUV2RGBConversionStandard.BT_601, performFullRange, isUYVY, pFrom, pTo);
                break;
            default:
                TranscodeFromYUVInternal(width, height, YUV2RGBConversionStandard.BT_601, performFullRange, isUYVY, pFrom, pTo);
                break;
        }
    }

#if NET45_OR_GREATER || NETSTANDARD || NETCOREAPP
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
    private static byte Clip(int value) =>
        value < 0 ? (byte)0 :
        value > 255 ? (byte)255 :
        (byte)value;

    public static int? GetRequiredBufferSize(
        int width, int height, NativeMethods.Compression compression)
    {
        switch (compression)
        {
            case NativeMethods.Compression.UYVY:
            case NativeMethods.Compression.YUYV:
            case NativeMethods.Compression.YUY2:
            case NativeMethods.Compression.HDYC:
                return width * height * 3;
            default:
                return null;
        }
    }

    public static unsafe void Transcode(
        int width, int height,
        NativeMethods.Compression compression, 
        YUV2RGBConversionStandard conversionStandard, 
        bool performFullRange,
        byte* pFrom, byte* pTo)
    {
        switch (compression, transcodeFormat)
        {
            case NativeMethods.Compression.UYVY:
            case NativeMethods.Compression.HDYC:
                TranscodeFromYUV(width, height, conversionStandard, true, performFullRange, pFrom, pTo);
                break;
            case NativeMethods.Compression.YUYV:
            case NativeMethods.Compression.YUY2:
                TranscodeFromYUV(width, height, conversionStandard, false, performFullRange, pFrom, pTo);
                break;
            default:
                throw new ArgumentException();
        }
    }
}
