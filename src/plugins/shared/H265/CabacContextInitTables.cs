//  The copyright in this software is being made available under the BSD
// License, included below. This software may be subject to other third party
// and contributor rights, including patent rights, and no such rights are
// granted under this license.
//
// Copyright (c) 2010-2026, ITU/ISO/IEC
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are met:
//
//  * Redistributions of source code must retain the above copyright notice,
//    this list of conditions and the following disclaimer.
//  * Redistributions in binary form must reproduce the above copyright notice,
//    this list of conditions and the following disclaimer in the documentation
//    and/or other materials provided with the distribution.
//  * Neither the name of the ITU/ISO/IEC nor the names of its contributors may
//    be used to endorse or promote products derived from this software without
//    specific prior written permission.
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
// AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
// IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
// ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS
// BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
// CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
// SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
// INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN
// CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
// ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF
// THE POSSIBILITY OF SUCH DAMAGE.

// Source: https://vcgit.hhi.fraunhofer.de/jvet/HM/-/raw/master/source/Lib/TLibCommon/ContextTables.h

namespace H265;

public static class CabacContextInitTables
{
  public const int CtxCount = 256;
  public const int InitTypeCount = 3;

  public static readonly byte[][] InitValue =
  [
    [
      153, 200, 139, 141, 157, 154, 154, 154, 154, 154, 154, 184, 154, 154, 154, 184,
       63, 154, 111, 111, 125, 110, 110,  94, 124, 108, 124, 107, 125, 141, 179, 153,
      125, 107, 125, 141, 179, 153, 125, 107, 125, 141, 179, 153, 125, 140, 139, 182,
      182, 152, 136, 152, 136, 153, 136, 139, 111, 136, 139, 111, 110, 110, 124, 125,
      140, 153, 125, 127, 140, 109, 111, 143, 127, 111,  79, 108, 123,  63, 110, 110,
      124, 125, 140, 153, 125, 127, 140, 109, 111, 143, 127, 111,  79, 108, 123,  63,
      140,  92, 137, 138, 140, 152, 138, 139, 153,  74, 149,  92, 139, 107, 122, 152,
      140, 179, 166, 182, 140, 227, 122, 197, 138, 153, 136, 167, 152, 152, 153, 138,
      138, 111, 141,  94, 138, 182, 154, 139, 139, 154, 154, 154, 154, 154, 154, 154,
      154, 154, 154, 154, 154, 154, 154, 154,  91, 171, 134, 141, 154, 154, 154, 154,
      154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154,
      154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154,
      154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154,
      154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154,
      154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154,
      154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154
    ],
    [
      153, 185, 107, 139, 126, 154, 197, 185, 201, 154, 149, 154, 139, 154, 154, 154,
      152,  79, 155, 154, 139, 153, 139, 123, 123,  63, 153, 166, 183, 140, 136, 153,
      154, 166, 183, 140, 136, 153, 154, 166, 183, 140, 136, 153, 154, 170, 153, 123,
      123, 107, 121, 107, 121, 167, 151, 183, 140, 151, 183, 140, 125, 110,  94, 110,
       95,  79, 125, 111, 110,  78, 110, 111, 111,  95,  94, 108, 123, 108, 125, 110,
       94, 110,  95,  79, 125, 111, 110,  78, 110, 111, 111,  95,  94, 108, 123, 108,
      154, 196, 196, 167, 154, 152, 167, 182, 182, 134, 149, 136, 153, 121, 136, 137,
      169, 194, 166, 167, 154, 167, 137, 182, 107, 167,  91, 122, 107, 167, 124, 138,
       94, 153, 111, 149, 107, 167, 154, 139, 139, 110, 122,  95,  79,  63,  31,  31,
      153, 153, 140, 198, 168, 154, 154, 154, 121, 140,  61, 154, 154, 154, 154, 154,
      154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154,
      154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154,
      154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154,
      154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154,
      154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154,
      154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154
    ],
    [
      153, 160, 107, 139, 126, 154, 197, 185, 201, 154, 134, 154, 139, 154, 154, 183,
      152,  79, 170, 154, 139, 153, 139, 123, 123,  63, 124, 166, 183, 140, 136, 153,
      154, 166, 183, 140, 136, 153, 154, 166, 183, 140, 136, 153, 154, 170, 153, 138,
      138, 122, 121, 122, 121, 167, 151, 183, 140, 151, 183, 140, 125, 110, 124, 110,
       95,  94, 125, 111, 111,  79, 125, 126, 111, 111,  79, 108, 123,  93, 125, 110,
      124, 110,  95,  94, 125, 111, 111,  79, 125, 126, 111, 111,  79, 108, 123,  93,
      154, 196, 167, 167, 154, 152, 167, 182, 182, 134, 149, 136, 153, 121, 136, 122,
      169, 208, 166, 167, 154, 152, 167, 182, 107, 167,  91, 107, 107, 167, 224, 167,
      122, 153, 111, 149,  92, 167, 154, 139, 139, 154, 137,  95,  79,  63,  31,  31,
      153, 153, 169, 198, 168, 154, 154, 154, 121, 140,  61, 154, 154, 154, 154, 154,
      154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154,
      154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154,
      154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154,
      154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154,
      154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154,
      154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154, 154
    ]
  ];
}
