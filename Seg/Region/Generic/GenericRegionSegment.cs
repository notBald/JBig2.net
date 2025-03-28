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
* GenericRegionSegment.java
* ---------------
*/
#endregion
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

using JBig2.Decoders;
using JBig2.Image;
using JBig2.Seg.PageInformation;

namespace JBig2.Seg.Region.Generic
{
    internal class GenericRegionSegment : RegionSegment
    {
        #region Variables and properties

        private GenericRegionFlags genericRegionFlags = new GenericRegionFlags();

        private bool inlineImage;

        private bool unknownLength = false;

        #endregion

        #region Init

        public GenericRegionSegment(JBIG2StreamDecoder streamDecoder, bool inlineImage)
            : base(streamDecoder)
        {
            this.inlineImage = inlineImage;
        }

        #endregion

        public override void readSegment()
        {
#if DEBUG
            if (JBIG2StreamDecoder.DEBUG)
                Debug.WriteLine("==== Reading Immediate Generic Region ====");
#endif
            base.readSegment();

            /** read text region Segment flags */
            readGenericRegionFlags();

            bool useMMR = genericRegionFlags.getFlagValue(GenericRegionFlags.MMR) != 0;
            int template = genericRegionFlags.getFlagValue(GenericRegionFlags.GB_TEMPLATE);

            short[] genericBAdaptiveTemplateX = new short[4];
            short[] genericBAdaptiveTemplateY = new short[4];

            if (!useMMR)
            {
                if (template == 0)
                {
                    genericBAdaptiveTemplateX[0] = readATValue();
                    genericBAdaptiveTemplateY[0] = readATValue();
                    genericBAdaptiveTemplateX[1] = readATValue();
                    genericBAdaptiveTemplateY[1] = readATValue();
                    genericBAdaptiveTemplateX[2] = readATValue();
                    genericBAdaptiveTemplateY[2] = readATValue();
                    genericBAdaptiveTemplateX[3] = readATValue();
                    genericBAdaptiveTemplateY[3] = readATValue();
                }
                else
                {
                    genericBAdaptiveTemplateX[0] = readATValue();
                    genericBAdaptiveTemplateY[0] = readATValue();
                }

                arithmeticDecoder.resetGenericStats(template, null);
                arithmeticDecoder.start();
            }

            bool typicalPredictionGenericDecodingOn = genericRegionFlags.getFlagValue(GenericRegionFlags.TPGDON) != 0;
            int length = _seg_head.DataLength;

            if (length == -1)
            {
                /** 
                 * length of data is unknown, so it needs to be determined through examination of the data.
                 * See 7.2.7 - Segment data length of the JBIG2 specification.
                 */

                unknownLength = true;

                short match1;
                short match2;

                if (useMMR)
                {
                    // look for 0x00 0x00 (0, 0)

                    match1 = 0;
                    match2 = 0;
                }
                else
                {
                    // look for 0xFF 0xAC (255, 172)

                    match1 = 255;
                    match2 = 172;
                }

                int bytesRead = 0;
                while (true)
                {
                    short bite1 = decoder.ReadByte();
                    bytesRead++;

                    if (bite1 == match1)
                    {
                        short bite2 = decoder.ReadByte();
                        bytesRead++;

                        if (bite2 == match2)
                        {
                            length = bytesRead - 2;
                            break;
                        }
                    }
                }

                decoder.movePointer(-bytesRead);
            }

            JBIG2Bitmap bitmap = new JBIG2Bitmap(regionBitmapWidth, regionBitmapHeight, arithmeticDecoder, huffmanDecoder, mmrDecoder);
            bitmap.clear(0);
            bitmap.readBitmap(useMMR, template, typicalPredictionGenericDecodingOn, false, null, genericBAdaptiveTemplateX, genericBAdaptiveTemplateY, useMMR ? 0 : length - 18);



            if (inlineImage)
            {
                PageInformationSegment pageSegment = decoder.findPageSegement(_seg_head.PageAssociation);
                JBIG2Bitmap pageBitmap = pageSegment.getPageBitmap();

                int extCombOp = regionFlags.getFlagValue(RegionFlags.EXTERNAL_COMBINATION_OPERATOR);

                if (pageSegment.getPageBitmapHeight() == -1 && regionBitmapYLocation + regionBitmapHeight > pageBitmap.getHeight())
                {
                    pageBitmap.expand(regionBitmapYLocation + regionBitmapHeight,
                            pageSegment.getPageInformationFlags().getFlagValue(PageInformationFlags.DEFAULT_PIXEL_VALUE));
                }

                pageBitmap.combine(bitmap, regionBitmapXLocation, regionBitmapYLocation, extCombOp);
            }
            else
            {
                bitmap.setBitmapNumber(SegmentHeader.SegmentNumber);
                decoder.appendBitmap(bitmap);
            }


            if (unknownLength)
            {
                decoder.movePointer(4);
            }
        }

        private void readGenericRegionFlags()
        {
            /** extract text region Segment flags */
            byte genericRegionFlagsField = decoder.ReadByte();

            genericRegionFlags.setFlags(genericRegionFlagsField);
#if DEBUG
            if (JBIG2StreamDecoder.DEBUG)
                Debug.WriteLine("generic region Segment flags = " + genericRegionFlagsField);
#endif
        }

        public GenericRegionFlags getGenericRegionFlags()
        {
            return genericRegionFlags;
        }
    }
}
