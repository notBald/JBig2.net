﻿#region Licsens
/**
* ===========================================
* Java Pdf Extraction Decoding Access Library
* ===========================================
*
* Project Info:  http://www.jpedal.org
* (C) Copyright 1997-2008, IDRsolutions and Contributors.
* Main Developer: Simon Barnett
*
* 	This file is part of JPedal
*
* Copyright (c) 2008, IDRsolutions
* All rights reserved.
*
* Redistribution and use in source and binary forms, with or without
* modification, are permitted provided that the following conditions are met:
*     * Redistributions of source code must retain the above copyright
*       notice, this list of conditions and the following disclaimer.
*     * Redistributions in binary form must reproduce the above copyright
*       notice, this list of conditions and the following disclaimer in the
*       documentation and/or other materials provided with the distribution.
*     * Neither the name of the IDRsolutions nor the
*       names of its contributors may be used to endorse or promote products
*       derived from this software without specific prior written permission.
*
* THIS SOFTWARE IS PROVIDED BY IDRsolutions ``AS IS'' AND ANY
* EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
* WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
* DISCLAIMED. IN NO EVENT SHALL IDRsolutions BE LIABLE FOR ANY
* DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
* (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
* LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
* ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
* (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
* SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
*
* Other JBIG2 image decoding implementations include
* jbig2dec (http://jbig2dec.sourceforge.net/)
* xpdf (http://www.foolabs.com/xpdf/)
* 
* The final draft JBIG2 specification can be found at http://www.jpeg.org/public/fcd14492.pdf
* 
* All three of the above resources were used in the writing of this software, with methodologies,
* processes and inspiration taken from all three.
*
* ---------------
* HalftoneRegionSegment.java
* ---------------
*/
#endregion
using JBig2.Decoders;
using JBig2.Image;
using JBig2.Seg.PageInformation;
using JBig2.Seg.Pattern;
using JBig2.Util;
using System.Diagnostics;

namespace JBig2.Seg.Region.Halftone
{
    internal class HalftoneRegionSegment : RegionSegment
    {
        #region Variables and properties

        private HalftoneRegionFlags halftoneRegionFlags = new HalftoneRegionFlags();

        private bool inlineImage;

        #endregion

        #region Init

        public HalftoneRegionSegment(JBIG2StreamDecoder streamDecoder, bool inlineImage)
            : base(streamDecoder)
        {
            this.inlineImage = inlineImage;
        }

        #endregion

        public override void readSegment()
        {
            base.readSegment();

            /** read text region Segment flags */
            readHalftoneRegionFlags();

            byte[] buf = new byte[4];
            decoder.readByte(buf);
            int gridWidth = BinaryOperation.getInt32(buf);

            buf = new byte[4];
            decoder.readByte(buf);
            int gridHeight = BinaryOperation.getInt32(buf);

            buf = new byte[4];
            decoder.readByte(buf);
            int gridX = BinaryOperation.getInt32(buf);

            buf = new byte[4];
            decoder.readByte(buf);
            int gridY = BinaryOperation.getInt32(buf);
#if DEBUG
            if (JBIG2StreamDecoder.DEBUG)
                Debug.WriteLine("grid pos and size = " + gridX + ',' + gridY + ' ' + gridWidth + ',' + gridHeight);
#endif
            buf = new byte[2];
            decoder.readByte(buf);
            int stepX = BinaryOperation.getInt16(buf);

            buf = new byte[2];
            decoder.readByte(buf);
            int stepY = BinaryOperation.getInt16(buf);
#if DEBUG
            if (JBIG2StreamDecoder.DEBUG)
                Debug.WriteLine("step size = " + stepX + ',' + stepY);
#endif
            int[] referedToSegments = _seg_head.ReferredToSegments;
            if (referedToSegments.Length != 1)
            {
                Debug.WriteLine("Error in halftone Segment. refSegs should == 1");
            }

            Segment segment = decoder.findSegment(referedToSegments[0]);
            if (segment.SegmentHeader.SegmentType != SegType.PATTERN_DICTIONARY)
            {
#if DEBUG
                if (JBIG2StreamDecoder.DEBUG)
                    Debug.WriteLine("Error in halftone Segment. bad symbol dictionary reference");
#endif
            }

            PatternDictionarySegment patternDictionarySegment = (PatternDictionarySegment)segment;

            int bitsPerValue = 0, i = 1;
            while (i < patternDictionarySegment.getSize())
            {
                bitsPerValue++;
                i <<= 1;
            }

            JBIG2Bitmap bitmap = patternDictionarySegment.getBitmaps()[0];
            int patternWidth = bitmap.getWidth();
            int patternHeight = bitmap.getHeight();
#if DEBUG
            if (JBIG2StreamDecoder.DEBUG)
                Debug.WriteLine("pattern size = " + patternWidth + ',' + patternHeight);
#endif
            bool useMMR = halftoneRegionFlags.getFlagValue(HalftoneRegionFlags.H_MMR) != 0;
            int template = halftoneRegionFlags.getFlagValue(HalftoneRegionFlags.H_TEMPLATE);

            if (!useMMR)
            {
                arithmeticDecoder.resetGenericStats(template, null);
                arithmeticDecoder.start();
            }

            int halftoneDefaultPixel = halftoneRegionFlags.getFlagValue(HalftoneRegionFlags.H_DEF_PIXEL);
            bitmap = new JBIG2Bitmap(regionBitmapWidth, regionBitmapHeight, arithmeticDecoder, huffmanDecoder, mmrDecoder);
            bitmap.clear(halftoneDefaultPixel);

            bool enableSkip = halftoneRegionFlags.getFlagValue(HalftoneRegionFlags.H_ENABLE_SKIP) != 0;

            JBIG2Bitmap skipBitmap = null;
            if (enableSkip)
            {
                skipBitmap = new JBIG2Bitmap(gridWidth, gridHeight, arithmeticDecoder, huffmanDecoder, mmrDecoder);
                skipBitmap.clear(0);
                for (int y = 0; y < gridHeight; y++)
                {
                    for (int x = 0; x < gridWidth; x++)
                    {
                        int xx = gridX + y * stepY + x * stepX;
                        int yy = gridY + y * stepX - x * stepY;

                        if (((xx + patternWidth) >> 8) <= 0 || (xx >> 8) >= regionBitmapWidth || ((yy + patternHeight) >> 8) <= 0 || (yy >> 8) >= regionBitmapHeight)
                        {
                            skipBitmap.setPixel(y, x, 1);
                        }
                    }
                }
            }

            int[] grayScaleImage = new int[gridWidth * gridHeight];

            short[] genericBAdaptiveTemplateX = new short[4], genericBAdaptiveTemplateY = new short[4];

            genericBAdaptiveTemplateX[0] = (short)(template <= 1 ? 3 : 2);
            genericBAdaptiveTemplateY[0] = -1;
            genericBAdaptiveTemplateX[1] = -3;
            genericBAdaptiveTemplateY[1] = -1;
            genericBAdaptiveTemplateX[2] = 2;
            genericBAdaptiveTemplateY[2] = -2;
            genericBAdaptiveTemplateX[3] = -2;
            genericBAdaptiveTemplateY[3] = -2;

            JBIG2Bitmap grayBitmap;

            for (int j = bitsPerValue - 1; j >= 0; --j)
            {
                grayBitmap = new JBIG2Bitmap(gridWidth, gridHeight, arithmeticDecoder, huffmanDecoder, mmrDecoder);

                grayBitmap.readBitmap(useMMR, template, false, enableSkip, skipBitmap, genericBAdaptiveTemplateX, genericBAdaptiveTemplateY, -1);

                i = 0;
                for (int row = 0; row < gridHeight; row++)
                {
                    for (int col = 0; col < gridWidth; col++)
                    {
                        int bit = grayBitmap.getPixel(col, row) ^ (grayScaleImage[i] & 1);
                        grayScaleImage[i] = (grayScaleImage[i] << 1) | bit;
                        i++;
                    }
                }
            }

            int combinationOperator = halftoneRegionFlags.getFlagValue(HalftoneRegionFlags.H_COMB_OP);

            i = 0;
            for (int col = 0; col < gridHeight; col++)
            {
                int xx = gridX + col * stepY;
                int yy = gridY + col * stepX;
                for (int row = 0; row < gridWidth; row++)
                {
                    if (!(enableSkip && skipBitmap.getPixel(col, row) == 1))
                    {
                        JBIG2Bitmap patternBitmap = patternDictionarySegment.getBitmaps()[grayScaleImage[i]];
                        bitmap.combine(patternBitmap, xx >> 8, yy >> 8, combinationOperator);
                    }

                    xx += stepX;
                    yy -= stepY;

                    i++;
                }
            }

            if (inlineImage)
            {
                PageInformationSegment pageSegment = decoder.findPageSegement(_seg_head.PageAssociation);
                JBIG2Bitmap pageBitmap = pageSegment.getPageBitmap();

                int externalCombinationOperator = regionFlags.getFlagValue(RegionFlags.EXTERNAL_COMBINATION_OPERATOR);
                pageBitmap.combine(bitmap, regionBitmapXLocation, regionBitmapYLocation, externalCombinationOperator);
            }
            else
            {
                bitmap.setBitmapNumber(SegmentHeader.SegmentNumber);
                decoder.appendBitmap(bitmap);
            }
        }

        private void readHalftoneRegionFlags()
        {
            /** extract text region Segment flags */
            byte halftoneRegionFlagsField = decoder.ReadByte();

            halftoneRegionFlags.setFlags(halftoneRegionFlagsField);
#if DEBUG
            if (JBIG2StreamDecoder.DEBUG)
                Debug.WriteLine("generic region Segment flags = " + halftoneRegionFlagsField);
#endif
        }

        public HalftoneRegionFlags getHalftoneRegionFlags()
        {
            return halftoneRegionFlags;
        }
    }
}
